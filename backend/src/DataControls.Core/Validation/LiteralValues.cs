using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using DataControls.Core.Catalog;

namespace DataControls.Core.Validation;

/// <summary>
/// Converts a literal JSON value to the CLR value of a field's data type - strictly, no guessing:
/// a number is never accepted for a date, the string "18" is not a number, an enum value must be one of the allowed values.
/// Used by the validator (to report errors) and later by the SQL renderer (to build typed parameters).
/// </summary>
public static class LiteralValues
{
    public const string DateFormat = "yyyy-MM-dd";

    /// <param name="value">On success: string, long, decimal, DateOnly or bool.</param>
    /// <param name="error">On failure: Hebrew message describing what was expected.</param>
    /// <param name="maxTextLength">The validator passes the configured limit; reading an already validated value does not limit it.</param>
    public static bool TryConvert(
        JsonElement element,
        FieldMeta field,
        [NotNullWhen(true)] out object? value,
        [NotNullWhen(false)] out string? error,
        int maxTextLength = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(field);
        value = null;
        error = null;

        switch (field.DataType)
        {
            case DataType.Text when element.ValueKind == JsonValueKind.String:
                var text = element.GetString()!;
                if (string.IsNullOrWhiteSpace(text))
                {
                    error = $"יש להזין ערך עבור '{field.DisplayName}' (לבדיקת ערך חסר השתמשו ב'ריק').";
                }
                else if (text.Length > maxTextLength)
                {
                    error = $"הערך עבור '{field.DisplayName}' ארוך מדי (עד {maxTextLength} תווים).";
                }
                else
                {
                    value = text;
                }

                break;

            case DataType.Integer when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var integer):
                value = integer;
                break;

            case DataType.Decimal when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number):
                value = number;
                break;

            case DataType.Date when element.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(element.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                value = date;
                break;

            case DataType.Boolean when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                value = element.GetBoolean();
                break;

            case DataType.Enum when element.ValueKind == JsonValueKind.String:
                var raw = element.GetString();
                var allowed = field.AllowedValues.FirstOrDefault(a => a.Value == raw);
                if (allowed is null)
                {
                    error = $"'{raw}' אינו ערך אפשרי עבור '{field.DisplayName}'. ערכים אפשריים: {string.Join(", ", field.AllowedValues.Select(a => a.Label))}.";
                }
                else
                {
                    value = allowed.Value;
                }

                break;
        }

        // A number that did not convert is out of range - unless it is a fraction for a whole-number field.
        if (value is null && error is null && element.ValueKind == JsonValueKind.Number
            && field.DataType is DataType.Integer or DataType.Decimal
            && !(field.DataType == DataType.Integer && element.TryGetDecimal(out var whole) && whole != decimal.Truncate(whole)))
        {
            error = $"הערך עבור '{field.DisplayName}' חורג מהטווח האפשרי.";
        }

        if (value is null && error is null)
        {
            error = $"הערך עבור '{field.DisplayName}' צריך להיות {ExpectedDescription(field.DataType)}.";
        }

        return value is not null;
    }

    private static string ExpectedDescription(DataType type) => type switch
    {
        DataType.Text => "טקסט",
        DataType.Integer => "מספר שלם",
        DataType.Decimal => "מספר",
        DataType.Date => $"תאריך בפורמט {DateFormat}",
        DataType.Boolean => "כן או לא",
        DataType.Enum => "אחד מהערכים האפשריים",
        _ => type.HebrewName(),
    };
}
