using System.Globalization;
using DataControls.Core.Catalog;

namespace DataControls.Core.Describing;

/// <summary>Formats values for business users: "15,000 ₪", "05/03/2021", "כן", enum labels, "ריק" for a missing value.</summary>
public static class HebrewFormat
{
    public const string Empty = "ריק";
    public const string Yes = "כן";
    public const string No = "לא";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Formats a value of <paramref name="field"/> (from a definition or from a database row).</summary>
    public static string Value(object? value, FieldMeta field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var text = value switch
        {
            null or DBNull => null,
            string s when field.DataType == DataType.Enum => field.AllowedValues.FirstOrDefault(a => a.Value == s)?.Label ?? s,
            string s => s.Trim().Length == 0 ? null : s,
            bool b => b ? Yes : No,
            DateOnly d => d.ToString("dd/MM/yyyy", Inv),
            DateTime dt => dt.ToString("dd/MM/yyyy", Inv),
            int or long or short or byte => Convert.ToInt64(value, Inv).ToString("#,0", Inv),
            decimal m => m.ToString("#,0.##", Inv),
            double or float => Convert.ToDecimal(value, Inv).ToString("#,0.##", Inv),
            _ => Convert.ToString(value, Inv),
        };

        if (text is null)
        {
            return Empty;
        }

        return field.Unit is { Length: > 0 } unit && field.DataType is DataType.Integer or DataType.Decimal
            ? $"{text} {unit}"
            : text;
    }
}
