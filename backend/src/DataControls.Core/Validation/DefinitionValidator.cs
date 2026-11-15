using System.Text.Json;
using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Operators;
using DataControls.Core.Paths;

namespace DataControls.Core.Validation;

/// <summary>
/// The gatekeeper: checks a control definition against the catalog before it is saved, previewed or run -
/// whether it was built in the UI or produced by an AI model. Reports ALL problems (not just the first),
/// each with the path of the offending node and a Hebrew message in business terms.
/// A definition that passes is guaranteed to be translatable to a safe query.
/// </summary>
public sealed class DefinitionValidator(ValidationLimits limits)
{
    public DefinitionValidator()
        : this(ValidationLimits.Default)
    {
    }

    /// <summary>The limits this validator enforces - planning a validated definition must use the same ones.</summary>
    public ValidationLimits Limits { get; } = limits;

    public ValidationResult Validate(ControlDefinition definition, MetadataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(catalog);

        var walk = new Walk(catalog, Limits);
        walk.Run(definition);
        return walk.Errors.Count == 0 ? ValidationResult.Valid : new ValidationResult(walk.Errors);
    }

    /// <summary>State of one validation run (errors and counters), so the validator itself stays stateless and thread-safe.</summary>
    private sealed class Walk(MetadataCatalog catalog, ValidationLimits limits)
    {
        private int _conditions;

        public List<ValidationError> Errors { get; } = [];

        public void Run(ControlDefinition definition)
        {
            if (definition.SchemaVersion != ControlDefinition.CurrentSchemaVersion)
            {
                Add("schemaVersion", ValidationErrorCode.UnsupportedSchemaVersion, $"גרסת הגדרה {definition.SchemaVersion} אינה נתמכת.");
            }

            if (!catalog.TryGetEntity(definition.RootEntity ?? string.Empty, out var root))
            {
                Add("rootEntity", ValidationErrorCode.UnknownEntity, $"סוג הנתונים '{definition.RootEntity}' אינו קיים.");
                return; // every other check depends on the root entity
            }

            if (definition.Filter is null || definition.Filter.Children is not { Count: > 0 })
            {
                Add("filter", ValidationErrorCode.EmptyFilter, "יש להגדיר לפחות תנאי אחד.");
                return;
            }

            Group(definition.Filter, root, "filter", depth: 1, insideRelated: false);

            if (_conditions > limits.MaxConditions)
            {
                Add("filter", ValidationErrorCode.TooManyConditions, $"הבקרה מכילה {_conditions} תנאים - המקסימום הוא {limits.MaxConditions}.");
            }
        }

        private void Group(GroupNode group, EntityMeta entity, string path, int depth, bool insideRelated)
        {
            if (depth > limits.MaxDepth)
            {
                Add(path, ValidationErrorCode.TooDeep, $"קינון עמוק מדי - עד {limits.MaxDepth} רמות.");
                return;
            }

            if (group.Children is not { Count: > 0 })
            {
                Add(path, ValidationErrorCode.EmptyGroup, "קבוצת תנאים ריקה - הוסיפו תנאי או הסירו את הקבוצה.");
                return;
            }

            for (var i = 0; i < group.Children.Count; i++)
            {
                var childPath = $"{path}.children[{i}]";
                switch (group.Children[i])
                {
                    case GroupNode nested:
                        Group(nested, entity, childPath, depth + 1, insideRelated);
                        break;
                    case ConditionNode condition:
                        _conditions++;
                        Condition(condition, entity, childPath);
                        break;
                    case RelatedNode related:
                        _conditions++;
                        Related(related, entity, childPath, depth + 1, insideRelated);
                        break;
                    default:
                        Add(childPath, ValidationErrorCode.MissingNode, "תנאי חסר.");
                        break;
                }
            }
        }

        private void Condition(ConditionNode condition, EntityMeta entity, string path)
        {
            if (!ResolveField(entity, condition.Field, $"{path}.field", out var field))
            {
                return;
            }

            var op = OperatorRegistry.Get(condition.Operator);
            if (!op.IsAllowedFor(field.Field.DataType))
            {
                Add($"{path}.operator", ValidationErrorCode.OperatorNotAllowedForType,
                    $"האפשרות '{op.LabelFor(field.Field.DataType)}' אינה מתאימה לשדה '{field.DisplayName}' ({field.Field.DataType.HebrewName()}).");
                return;
            }

            var operandPath = $"{path}.operand";
            switch (op.Shape, condition.Operand)
            {
                case (OperandShape.None, null):
                    return;
                case (OperandShape.None, _):
                    Add(operandPath, ValidationErrorCode.OperandNotExpected, $"האפשרות '{op.Label}' אינה מקבלת ערך.");
                    return;
                case (_, null):
                    Add(operandPath, ValidationErrorCode.OperandMissing, $"יש להזין ערך עבור '{field.DisplayName}'.");
                    return;
                case (_, FieldOperand other):
                    FieldOperand(op, field, other, entity, operandPath);
                    return;
                case (_, LiteralOperand literal):
                    Literal(op, field, literal.Value, operandPath);
                    return;
            }
        }

        private void FieldOperand(OperatorInfo op, ResolvedField left, FieldOperand operand, EntityMeta entity, string path)
        {
            if (!op.AllowsFieldOperand)
            {
                Add(path, ValidationErrorCode.FieldOperandNotAllowed, $"לא ניתן להשוות לשדה אחר באפשרות '{op.Label}'.");
                return;
            }

            if (!ResolveField(entity, operand.Field, $"{path}.field", out var right))
            {
                return;
            }

            if (right.Path == left.Path)
            {
                Add(path, ValidationErrorCode.SameField, $"לא ניתן להשוות את '{left.DisplayName}' לעצמו.");
            }
            else if (!AreComparable(left.Field, right.Field))
            {
                Add(path, ValidationErrorCode.IncompatibleFieldTypes,
                    $"לא ניתן להשוות את '{left.DisplayName}' ({left.Field.DataType.HebrewName()}) ל'{right.DisplayName}' ({right.Field.DataType.HebrewName()}).");
            }
        }

        private void Literal(OperatorInfo op, ResolvedField field, JsonElement value, string path)
        {
            switch (op.Shape)
            {
                case OperandShape.Value:
                    Scalar(field, value, $"{path}.value");
                    break;

                case OperandShape.Range:
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
                    {
                        Add($"{path}.value", ValidationErrorCode.InvalidRange, $"יש להזין שני ערכים (מ- ועד) עבור '{field.DisplayName}'.");
                        return;
                    }

                    var from = Scalar(field, value[0], $"{path}.value[0]");
                    var to = Scalar(field, value[1], $"{path}.value[1]");
                    if (from is IComparable a && to is not null && a.CompareTo(to) > 0)
                    {
                        Add($"{path}.value", ValidationErrorCode.InvalidRange, $"בטווח עבור '{field.DisplayName}' ערך ההתחלה גדול מערך הסיום.");
                    }

                    break;

                case OperandShape.List:
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                    {
                        Add($"{path}.value", ValidationErrorCode.EmptyList, $"יש לבחור לפחות ערך אחד עבור '{field.DisplayName}'.");
                        return;
                    }

                    if (value.GetArrayLength() > limits.MaxListSize)
                    {
                        Add($"{path}.value", ValidationErrorCode.ListTooLong, $"ניתן לבחור עד {limits.MaxListSize} ערכים.");
                        return;
                    }

                    var index = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        Scalar(field, item, $"{path}.value[{index++}]");
                    }

                    break;
            }
        }

        private object? Scalar(ResolvedField field, JsonElement element, string path)
        {
            if (LiteralValues.TryConvert(element, field.Field, out var value, out var error, limits.MaxTextValueLength))
            {
                return value;
            }

            Add(path, ValidationErrorCode.InvalidValue, error);
            return null;
        }

        private void Related(RelatedNode related, EntityMeta entity, string path, int depth, bool insideRelated)
        {
            if (insideRelated)
            {
                Add(path, ValidationErrorCode.NestedRelatedNotAllowed, "לא ניתן להגדיר תנאי 'יש / אין' בתוך תנאי 'יש / אין'.");
                return;
            }

            if (depth > limits.MaxDepth)
            {
                Add(path, ValidationErrorCode.TooDeep, $"קינון עמוק מדי - עד {limits.MaxDepth} רמות.");
                return;
            }

            if (!catalog.TryGetRelation(entity.Key, related.Relation ?? string.Empty, out var relation))
            {
                Add($"{path}.relation", ValidationErrorCode.UnknownRelation, $"ל{entity.DisplayName} אין קשר בשם '{related.Relation}'.");
                return;
            }

            if (relation.Cardinality != Cardinality.OneToMany)
            {
                Add($"{path}.relation", ValidationErrorCode.RelationNotOneToMany,
                    $"'{relation.DisplayName}' הוא קשר יחיד - השתמשו בשדות של {relation.DisplayName} ישירות בתנאי רגיל.");
                return;
            }

            // An empty or missing "where" means "any / no related record at all".
            if (related.Where is { Children.Count: > 0 } where)
            {
                Group(where, catalog.GetEntity(relation.ToEntityKey), $"{path}.where", depth + 1, insideRelated: true);
            }
        }

        private bool ResolveField(EntityMeta entity, string? path, string errorPath, out ResolvedField field)
        {
            if (FieldPathResolver.TryResolve(catalog, entity.Key, path, out var resolved, out var error, limits.MaxRelationHops))
            {
                field = resolved;
                return true;
            }

            Add(errorPath, MapPathError(error.Code), error.Message);
            field = null!;
            return false;
        }

        private static bool AreComparable(FieldMeta left, FieldMeta right) =>
            left.DataType == right.DataType
            || (IsNumeric(left.DataType) && IsNumeric(right.DataType));

        private static bool IsNumeric(DataType type) => type is DataType.Integer or DataType.Decimal;

        private static ValidationErrorCode MapPathError(PathErrorCode code) => code switch
        {
            PathErrorCode.UnknownEntity => ValidationErrorCode.UnknownEntity,
            PathErrorCode.EmptyPath => ValidationErrorCode.EmptyPath,
            PathErrorCode.InvalidFormat => ValidationErrorCode.InvalidPath,
            PathErrorCode.UnknownRelation => ValidationErrorCode.UnknownRelation,
            PathErrorCode.OneToManyInPath => ValidationErrorCode.OneToManyInPath,
            PathErrorCode.TooManyRelations => ValidationErrorCode.TooManyRelations,
            PathErrorCode.RelationWithoutField => ValidationErrorCode.RelationWithoutField,
            PathErrorCode.UnknownField => ValidationErrorCode.UnknownField,
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        };

        private void Add(string path, ValidationErrorCode code, string message) => Errors.Add(new(path, code, message));
    }
}
