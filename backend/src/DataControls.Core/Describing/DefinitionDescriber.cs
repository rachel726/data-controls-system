using System.Text.Json;
using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Paths;
using DataControls.Core.Validation;

namespace DataControls.Core.Describing;

/// <summary>
/// Turns a VALIDATED definition into one Hebrew sentence in business terms, e.g.
/// "גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000 ₪". Shown live in the editor and on the AI approval screen
/// (the UI prefixes it with "רשומה תיחשב חריגה כאשר:").
/// </summary>
public static class DefinitionDescriber
{
    public static string Describe(ControlDefinition definition, MetadataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(catalog);

        return Group(definition.Filter, catalog.GetEntity(definition.RootEntity), catalog, nested: false);
    }

    /// <summary>Describes one group of conditions on <paramref name="entity"/> (used for related-records reasons).</summary>
    internal static string Group(GroupNode group, EntityMeta entity, MetadataCatalog catalog, bool nested)
    {
        var parts = group.Children.Select(child => child switch
        {
            GroupNode g => Group(g, entity, catalog, nested: true),
            ConditionNode c => Condition(c, entity, catalog),
            RelatedNode r => Related(r, entity, catalog),
            _ => throw new InvalidOperationException("Describe a validated definition only."),
        }).ToList();

        var text = string.Join(group.Logic == LogicalOperator.And ? HebrewPhrases.And : HebrewPhrases.Or, parts);
        return nested && parts.Count > 1 ? $"({text})" : text;
    }

    private static string Condition(ConditionNode condition, EntityMeta entity, MetadataCatalog catalog)
    {
        var field = Resolve(catalog, entity, condition.Field);

        if (condition.Operand is LiteralOperand { Value.ValueKind: JsonValueKind.True or JsonValueKind.False } flag
            && HebrewPhrases.BooleanStatement(field, condition.Operator, flag.Value.GetBoolean()) is { } statement)
        {
            return statement;
        }

        IReadOnlyList<string> values = condition.Operand switch
        {
            null => [],
            FieldOperand other => [HebrewPhrases.FieldName(Resolve(catalog, entity, other.Field))],
            LiteralOperand { Value.ValueKind: JsonValueKind.Array } list => [.. list.Value.EnumerateArray().Select(v => Literal(v, field.Field))],
            LiteralOperand literal => [Literal(literal.Value, field.Field)],
            _ => throw new InvalidOperationException("Unknown operand."),
        };

        return HebrewPhrases.Condition(HebrewPhrases.FieldName(field), field.Field, condition.Operator, values, condition.Operand is FieldOperand);
    }

    private static string Related(RelatedNode related, EntityMeta entity, MetadataCatalog catalog)
    {
        if (!catalog.TryGetRelation(entity.Key, related.Relation, out var relation))
        {
            throw new InvalidOperationException("Describe a validated definition only.");
        }

        return Related(related, relation, catalog.GetEntity(relation.ToEntityKey), catalog);
    }

    /// <summary>
    /// "אין אף עובד פעיל", "יש עובדים שמתקיים בהם: גיל קטן מ-18". In an "and" group, the related record's own
    /// yes/no conditions describe the record ("עובד פעיל"); the rest follow as its conditions.
    /// Shared with the violation reasons, so both say the same.
    /// </summary>
    internal static string Related(RelatedNode related, RelationMeta relation, EntityMeta target, MetadataCatalog catalog)
    {
        var children = related.Where?.Children ?? [];
        var traits = new List<string>();
        var rest = new List<FilterNode>();
        foreach (var child in children)
        {
            if (related.Where!.Logic == LogicalOperator.And && child is ConditionNode condition && Trait(condition, target, catalog) is { } trait)
            {
                traits.Add(trait);
            }
            else
            {
                rest.Add(child);
            }
        }

        var where = rest.Count > 0 ? Group(related.Where! with { Children = rest }, target, catalog, nested: false) : null;
        return HebrewPhrases.Related(related.Quantifier, relation, target, traits, where, related.Where?.Logic != LogicalOperator.Or);
    }

    private static string? Trait(ConditionNode condition, EntityMeta target, MetadataCatalog catalog) =>
        condition.Operand is LiteralOperand { Value.ValueKind: JsonValueKind.True or JsonValueKind.False } flag
            ? HebrewPhrases.Trait(Resolve(catalog, target, condition.Field), condition.Operator, flag.Value.GetBoolean())
            : null;

    private static ResolvedField Resolve(MetadataCatalog catalog, EntityMeta entity, string path) =>
        FieldPathResolver.TryResolve(catalog, entity.Key, path, out var resolved, out _)
            ? resolved
            : throw new InvalidOperationException($"Describe a validated definition only ('{path}').");

    private static string Literal(JsonElement element, FieldMeta field) =>
        LiteralValues.TryConvert(element, field, out var value, out _)
            ? HebrewFormat.Value(value, field)
            : throw new InvalidOperationException("Describe a validated definition only.");
}
