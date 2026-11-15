using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Planning;
using DataControls.Core.Sql;

namespace DataControls.Core.Describing;

/// <summary>
/// Explains why a record is a violation: lists the conditions whose flag column is 1 in the result row,
/// each with the record's ACTUAL value, e.g. "גיל (16) קטן מ-18", "שכר חודשי (13,000 ₪) גדול משכר חודשי של מנהל ישיר (12,000 ₪)".
/// In an OR group only the branch that actually matched is listed.
/// </summary>
public static class ViolationReasonBuilder
{
    /// <param name="row">One result row: column key → value, including the flag columns (__c0, __c1, …).</param>
    public static IReadOnlyList<string> Build(QueryModel model, IReadOnlyDictionary<string, object?> row, MetadataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(catalog);

        return [.. model.Flags.Where(f => IsSet(row, SqlServerRenderer.FlagColumn(f.Index))).Select(f => Reason(f, row, catalog))];
    }

    private static string Reason(ConditionFlag flag, IReadOnlyDictionary<string, object?> row, MetadataCatalog catalog) =>
        flag.Node switch
        {
            RelatedNode related => RelatedReason(related, flag, catalog),
            ConditionNode condition => ConditionReason(condition, flag, row),
            _ => throw new InvalidOperationException($"Unexpected flag node {flag.Node.GetType().Name}."),
        };

    /// <summary>"אין אף עובד פעיל" - a fact about the record, worded like the description.</summary>
    private static string RelatedReason(RelatedNode related, ConditionFlag flag, MetadataCatalog catalog) =>
        DefinitionDescriber.Related(related, flag.Relation!, flag.RelatedEntity!, catalog);

    private static string ConditionReason(ConditionNode condition, ConditionFlag flag, IReadOnlyDictionary<string, object?> row)
    {
        var field = flag.Field!;
        if (flag.Operand is ValueOperand { Value: bool literal }
            && HebrewPhrases.BooleanStatement(field, condition.Operator, literal) is { } statement)
        {
            return statement;
        }

        var actual = HebrewFormat.Value(row.GetValueOrDefault(flag.ColumnKey!), field.Field);

        // "דואר אלקטרוני ריק" / "עובד פעיל: כן" - here the actual value adds nothing.
        var showActual = condition.Operator != ConditionOperator.IsEmpty
            && !(condition.Operator == ConditionOperator.Eq && flag.Operand is ValueOperand);   // "equals 18": the value is the actual one
        var name = HebrewPhrases.FieldName(field);
        var subject = showActual ? $"{name} ({actual})" : name;

        IReadOnlyList<string> values = flag.Operand switch
        {
            null => [],
            ValueOperand v => [HebrewFormat.Value(v.Value, field.Field)],
            RangeOperand r => [HebrewFormat.Value(r.From, field.Field), HebrewFormat.Value(r.To, field.Field)],
            ListOperand l => [.. l.Values.Select(v => HebrewFormat.Value(v, field.Field))],
            FieldOperandRef => [$"{HebrewPhrases.FieldName(flag.CompareField!)} ({HebrewFormat.Value(row.GetValueOrDefault(flag.CompareColumnKey!), flag.CompareField!.Field)})"],
            _ => throw new InvalidOperationException("Unknown operand."),
        };

        return HebrewPhrases.Condition(subject, field.Field, condition.Operator, values, flag.Operand is FieldOperandRef);
    }

    private static bool IsSet(IReadOnlyDictionary<string, object?> row, string flagColumn) =>
        row.GetValueOrDefault(flagColumn) switch
        {
            int i => i == 1,
            long l => l == 1,
            bool b => b,
            _ => false,
        };
}
