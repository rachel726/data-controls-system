namespace DataControls.Core.Catalog;

public static class DataTypeNames
{
    /// <summary>Hebrew name of a data type, for user-facing messages.</summary>
    public static string HebrewName(this DataType type) => type switch
    {
        DataType.Text => "טקסט",
        DataType.Integer => "מספר שלם",
        DataType.Decimal => "מספר",
        DataType.Date => "תאריך",
        DataType.Boolean => "כן/לא",
        DataType.Enum => "רשימת ערכים",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
