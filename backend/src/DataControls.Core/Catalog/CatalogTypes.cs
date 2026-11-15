using System.Diagnostics.CodeAnalysis;

namespace DataControls.Core.Catalog;

/// <summary>Business data type of a field. Drives allowed operators, value parsing and formatting.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "Business type names are part of the contract (meta.Fields CHECK constraint, JSON, UI).")]
public enum DataType
{
    Text,
    Integer,
    Decimal,
    Date,
    Boolean,
    Enum,
}

/// <summary>
/// Closed set of declarative column transforms implemented by the SQL renderer.
/// Metadata can never contain free SQL - only one of these names.
/// </summary>
public enum FieldTransform
{
    /// <summary>The column as-is.</summary>
    None,

    /// <summary>Whole years from the column's date until today (e.g. age from birth date).</summary>
    YearsSince,

    /// <summary>Source column + ' ' + <see cref="FieldMeta.TransformArg"/> column (e.g. first + last name).</summary>
    FullName,
}

/// <summary>Direction of a relation between two entities.</summary>
public enum Cardinality
{
    /// <summary>Each source record points to at most one target (e.g. employee → department). Usable in field paths.</summary>
    ManyToOne,

    /// <summary>Each source record has many targets (e.g. department → employees). Usable only in a "related records" condition.</summary>
    OneToMany,
}

/// <summary>Grammatical gender of a Hebrew noun.</summary>
public enum GrammaticalGender
{
    Masculine,
    Feminine,
}

/// <summary>An allowed value of an <see cref="DataType.Enum"/> field, with its business label.</summary>
public sealed record AllowedValue(string Value, string Label);
