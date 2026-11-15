using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Operators;
using DataControls.Core.Paths;

namespace DataControls.Core.Describing;

/// <summary>
/// The shared Hebrew grammar of conditions, used by both the definition description and the violation reasons,
/// so a control is always worded the same way.
/// </summary>
internal static class HebrewPhrases
{
    public const string And = " וגם ";
    public const string Or = " או ";

    /// <summary>
    /// "גיל קטן מ-18", "עובד פעיל: כן", "דואר אלקטרוני ריק", "שכר חודשי בין 5,000 ₪ ל-9,000 ₪", "שם פרטי שווה ל"רחל"".
    /// A free-text value is quoted, so it is never read as part of the sentence ("שווה ל"רחל"", not "שווה לרחל").
    /// </summary>
    /// <param name="subject">The field wording, optionally with its actual value: "גיל" or "גיל (16)".</param>
    /// <param name="values">Formatted operand(s): one value, [from, to], a list, or a compared field. Empty for "is empty".</param>
    public static string Condition(string subject, FieldMeta field, ConditionOperator op, IReadOnlyList<string> values, bool operandIsField)
    {
        var info = OperatorRegistry.Get(op);
        var label = info.LabelFor(field.DataType);
        if (field.DataType == DataType.Text && !operandIsField)
        {
            values = [.. values.Select(v => $"\"{v}\"")];
        }

        return op switch
        {
            // a yes/no field named as a statement ("עובד פעיל") reads as a sentence only with a colon
            ConditionOperator.IsEmpty or ConditionOperator.IsNotEmpty when field.DataType == DataType.Boolean => $"{subject}: {label}",
            ConditionOperator.IsEmpty or ConditionOperator.IsNotEmpty => $"{subject} {label}",
            ConditionOperator.Eq when field.DataType == DataType.Boolean && !operandIsField => $"{subject}: {values[0]}",
            ConditionOperator.Between => $"{subject} {label} {values[0]} {Attach("ל", values[1])}",
            ConditionOperator.In => $"{subject} {label}: {string.Join(", ", values)}",
            ConditionOperator.Contains => $"{subject} {label} {values[0]}",
            _ => $"{subject} {Attach(label, values[0])}",
        };
    }

    /// <summary>
    /// A yes/no condition worded as a statement from the catalog: "עובד פעיל", "מחלקה אינה פעילה",
    /// "מחלקה של מנהל ישיר אינה פעילה". Null when the field has no wording or the condition is not "= yes/no".
    /// </summary>
    public static string? BooleanStatement(ResolvedField field, ConditionOperator op, object? literal)
    {
        if (field.Field is not { TrueText: { } trueText, FalseText: { } falseText }
            || op is not (ConditionOperator.Eq or ConditionOperator.Neq)
            || literal is not bool value)
        {
            return null;
        }

        var subject = field.RelationChain.Count == 0 ? field.Entity.DisplayName : Owner(field);
        return $"{subject} {(value == (op == ConditionOperator.Eq) ? trueText : falseText)}";
    }

    /// <summary>"שכר חודשי", "שכר חודשי של מנהל ישיר", "קוד מחלקה של מחלקה של מנהל ישיר".</summary>
    public static string FieldName(ResolvedField field) => field.DisplayName;

    /// <summary>
    /// "יש פרויקטים", "אין עובדים עם גיל קטן מ-18", "אין אף עובד פעיל", "יש עובד שאינו פעיל עם גיל קטן מ-18";
    /// conditions joined by "או" keep "שמתקיים בהם:" so the "או" cannot be read as joining "יש / אין".
    /// </summary>
    /// <param name="target">The related entity - its name, and its gender for "בהם" / "בהן".</param>
    /// <param name="traits">The record's own yes/no wording: "פעיל" (said of the record) or "שאינו פעיל".</param>
    /// <param name="whereText">The other conditions on the related records, or null.</param>
    /// <param name="whereIsAnd">Whether those conditions must all hold ("עם") or one of them ("שמתקיים בהם:").</param>
    public static string Related(
        RelatedQuantifier quantifier, RelationMeta relation, EntityMeta target, IReadOnlyList<string> traits, string? whereText, bool whereIsAnd)
    {
        var head = traits.Count == 0
            ? $"{(quantifier == RelatedQuantifier.None ? "אין" : "יש")} {relation.DisplayName}"
            : $"{(quantifier == RelatedQuantifier.None ? "אין אף" : "יש")} {target.DisplayName} {string.Join(' ', traits)}";
        if (whereText is null)
        {
            return head;
        }

        return whereIsAnd
            ? $"{head} עם {whereText}"
            : $"{head} שמתקיים {(target.Gender == GrammaticalGender.Feminine ? "בהן" : "בהם")}: {whereText}";
    }

    /// <summary>
    /// The yes/no wording of a related record's own field, said of the record: "פעיל" for the catalog's yes text,
    /// "שאינו פעיל" for its no text (a negative is joined as a clause). Null when the condition is not one.
    /// </summary>
    public static string? Trait(ResolvedField field, ConditionOperator op, object? literal)
    {
        if (field.RelationChain.Count > 0
            || field.Field is not { TrueText: { } trueText, FalseText: { } falseText }
            || op is not (ConditionOperator.Eq or ConditionOperator.Neq)
            || literal is not bool value)
        {
            return null;
        }

        return value == (op == ConditionOperator.Eq) ? trueText : $"ש{falseText}";
    }

    /// <summary>The record a path leads to, nearest relation first: "מחלקה של מנהל ישיר".</summary>
    private static string Owner(ResolvedField field) =>
        string.Join(" של ", field.RelationChain.Reverse().Select(r => r.DisplayName));

    /// <summary>
    /// The Hebrew prefixes "מ"/"ל" join the following word directly ("גדול משכר חודשי"),
    /// and a number or symbol with a hyphen ("קטן מ-18", "ל-9,000 ₪").
    /// </summary>
    private static string Attach(string label, string value)
    {
        var isPrefix = label.EndsWith(" מ", StringComparison.Ordinal) || label.EndsWith(" ל", StringComparison.Ordinal) || label is "מ" or "ל";
        if (!isPrefix)
        {
            return $"{label} {value}";
        }

        // a word or a quoted value joins the prefix ("משכר", "ל"רחל""), a number or symbol takes a hyphen ("מ-18")
        return value.Length > 0 && value[0] is (>= 'א' and <= 'ת') or '"' ? $"{label}{value}" : $"{label}-{value}";
    }
}
