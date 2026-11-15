using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Operators;
using DataControls.Core.Paths;
using DataControls.Core.Validation;

namespace DataControls.Core.Planning;

/// <summary>
/// Translates a VALIDATED control definition into a <see cref="QueryModel"/>:
/// resolves field paths to deduplicated joins, builds the predicate tree with typed values,
/// assigns one flag per root-level condition and decides the result columns.
/// Deterministic: the same definition always yields the same aliases and column order.
/// </summary>
public static class QueryPlanner
{
    public const string RootAlias = "r";

    /// <exception cref="PlanningException">The definition was not validated (should never happen in the app flow).</exception>
    public static QueryModel Plan(ControlDefinition definition, MetadataCatalog catalog, ValidationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(limits);

        if (!catalog.TryGetEntity(definition.RootEntity, out var rootEntity))
        {
            throw new PlanningException($"Unknown root entity '{definition.RootEntity}'.");
        }

        var context = new PlanContext(catalog, limits);
        var rootScope = new Scope(context, rootEntity, Source(rootEntity, RootAlias), joinPrefix: "j");

        var filter = rootScope.Group(definition.Filter, "filter", trackFlags: true);
        var title = rootScope.Field(rootEntity.DisplayFieldKey);

        return new QueryModel(
            rootEntity,
            rootScope.Source,
            rootScope.Joins,
            filter,
            new FieldRef(title.Alias, title.Resolved.Field),
            context.BuildColumns(rootScope, rootEntity),
            context.Flags);
    }

    /// <summary>The flattened column key of a field path: "department.isActive" → "department__isActive".</summary>
    public static string ColumnKey(string path) => path.Replace(".", "__", StringComparison.Ordinal);

    private static TableSource Source(EntityMeta entity, string alias) =>
        new(entity.SchemaName, entity.TableName, alias, entity.PrimaryKeyColumn);

    /// <summary>State shared by the whole plan: flags and fields referenced by root-level conditions.</summary>
    private sealed class PlanContext(MetadataCatalog catalog, ValidationLimits limits)
    {
        private readonly List<(ResolvedField Field, FieldRef Expression)> _referenced = [];
        private int _subqueries;

        public MetadataCatalog Catalog { get; } = catalog;
        public ValidationLimits Limits { get; } = limits;
        public List<ConditionFlag> Flags { get; } = [];

        public string NextSubqueryAlias() => $"x{++_subqueries}";

        public string Reference(ResolvedField field, FieldRef expression)
        {
            if (!_referenced.Any(r => r.Field.Path == field.Path))
            {
                _referenced.Add((field, expression));
            }

            return ColumnKey(field.Path);
        }

        /// <summary>Visible fields of the root entity first (catalog order), then fields used in conditions (definition order).</summary>
        public List<OutputColumn> BuildColumns(Scope root, EntityMeta rootEntity)
        {
            var columns = new List<OutputColumn>();
            var referencedPaths = _referenced.Select(r => r.Field.Path).ToHashSet(StringComparer.Ordinal);

            foreach (var field in rootEntity.Fields.Where(f => f.IsVisibleInResults).OrderBy(f => f.SortOrder))
            {
                var resolved = root.Field(field.Key);
                columns.Add(new OutputColumn(ColumnKey(field.Key), resolved.Resolved, new FieldRef(resolved.Alias, field),
                    IsConditionField: referencedPaths.Contains(field.Key)));
            }

            foreach (var (field, expression) in _referenced.Where(r => columns.All(c => c.Field.Path != r.Field.Path)))
            {
                columns.Add(new OutputColumn(ColumnKey(field.Path), field, expression, IsConditionField: true));
            }

            return columns;
        }
    }

    /// <summary>One FROM scope: the root query, or the inside of one EXISTS subquery. Owns its joins and aliases.</summary>
    private sealed class Scope(PlanContext context, EntityMeta entity, TableSource source, string joinPrefix)
    {
        private readonly Dictionary<string, string> _aliasByRelationPath = new(StringComparer.Ordinal) { [""] = source.Alias };

        public TableSource Source { get; } = source;
        public List<Join> Joins { get; } = [];

        public GroupPredicate Group(GroupNode group, string path, bool trackFlags)
        {
            var children = new List<Predicate>(group.Children.Count);
            for (var i = 0; i < group.Children.Count; i++)
            {
                var childPath = $"{path}.children[{i}]";
                children.Add(group.Children[i] switch
                {
                    GroupNode nested => Group(nested, childPath, trackFlags),
                    ConditionNode condition => Condition(condition, childPath, trackFlags),
                    RelatedNode related when trackFlags => Related(related, childPath),
                    _ => throw new PlanningException($"Unexpected node at {childPath} - validate the definition first."),
                });
            }

            return new GroupPredicate(group.Logic, children);
        }

        public (string Alias, ResolvedField Resolved) Field(string path)
        {
            if (!FieldPathResolver.TryResolve(context.Catalog, entity.Key, path, out var resolved, out var error, context.Limits.MaxRelationHops))
            {
                throw new PlanningException($"Field '{path}' cannot be resolved on '{entity.Key}': {error.Code} - validate the definition first.");
            }

            return (AliasFor(resolved), resolved);
        }

        private ComparisonPredicate Condition(ConditionNode condition, string path, bool trackFlags)
        {
            var (alias, left) = Field(condition.Field);
            var leftRef = new FieldRef(alias, left.Field);

            PlannedOperand? operand = null;
            ResolvedField? compareField = null;
            FieldRef? compareRef = null;

            switch (condition.Operand)
            {
                case FieldOperand other:
                    var (otherAlias, right) = Field(other.Field);
                    compareField = right;
                    compareRef = new FieldRef(otherAlias, right.Field);
                    operand = new FieldOperandRef(compareRef);
                    break;
                case LiteralOperand literal:
                    operand = Literal(OperatorRegistry.Get(condition.Operator).Shape, left.Field, literal, path);
                    break;
            }

            int? flag = null;
            if (trackFlags)
            {
                flag = context.Flags.Count;
                context.Flags.Add(new ConditionFlag(flag.Value, path, condition,
                    left, context.Reference(left, leftRef), operand,
                    compareField, compareField is null ? null : context.Reference(compareField, compareRef!),
                    Relation: null, RelatedEntity: null));
            }

            return new ComparisonPredicate(flag, leftRef, condition.Operator, operand);
        }

        private ExistsPredicate Related(RelatedNode related, string path)
        {
            if (!context.Catalog.TryGetRelation(entity.Key, related.Relation, out var relation)
                || relation.Cardinality != Cardinality.OneToMany)
            {
                throw new PlanningException($"Relation '{related.Relation}' is not a one-to-many relation of '{entity.Key}' - validate the definition first.");
            }

            var target = context.Catalog.GetEntity(relation.ToEntityKey);
            var alias = context.NextSubqueryAlias();
            var inner = new Scope(context, target, Source(target, alias), joinPrefix: $"{alias}_j");

            var where = related.Where is { Children.Count: > 0 } w
                ? inner.Group(w, $"{path}.where", trackFlags: false)
                : null;

            var flag = context.Flags.Count;
            context.Flags.Add(new ConditionFlag(flag, path, related, Field: null, ColumnKey: null, Operand: null,
                CompareField: null, CompareColumnKey: null, relation, target));

            return new ExistsPredicate(flag, related.Quantifier == RelatedQuantifier.None, inner.Source,
                relation.ToColumn, Source.Alias, relation.FromColumn, inner.Joins, where);
        }

        /// <summary>Alias of the table that owns the field; adds (once) the LEFT JOINs along its relation chain.</summary>
        private string AliasFor(ResolvedField field)
        {
            var parentAlias = Source.Alias;
            var pathKey = "";

            foreach (var relation in field.RelationChain)
            {
                pathKey = pathKey.Length == 0 ? relation.Key : $"{pathKey}.{relation.Key}";
                if (!_aliasByRelationPath.TryGetValue(pathKey, out var alias))
                {
                    alias = $"{joinPrefix}{Joins.Count + 1}";
                    var target = context.Catalog.GetEntity(relation.ToEntityKey);
                    Joins.Add(new Join(Source(target, alias), relation.ToColumn, parentAlias, relation.FromColumn, pathKey));
                    _aliasByRelationPath[pathKey] = alias;
                }

                parentAlias = alias;
            }

            return parentAlias;
        }

        private static PlannedOperand Literal(OperandShape shape, FieldMeta field, LiteralOperand literal, string path)
        {
            object Convert(System.Text.Json.JsonElement element) =>
                LiteralValues.TryConvert(element, field, out var value, out var error)
                    ? value
                    : throw new PlanningException($"Invalid value at {path}: {error} - validate the definition first.");

            return shape switch
            {
                OperandShape.Value => new ValueOperand(Convert(literal.Value)),
                OperandShape.Range => new RangeOperand(Convert(literal.Value[0]), Convert(literal.Value[1])),
                OperandShape.List => new ListOperand([.. literal.Value.EnumerateArray().Select(Convert)]),
                _ => throw new PlanningException($"Operator at {path} takes no value - validate the definition first."),
            };
        }
    }
}

/// <summary>The definition cannot be planned. Indicates a programming error: definitions must be validated first.</summary>
public sealed class PlanningException(string message) : Exception(message);
