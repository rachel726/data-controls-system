using System.Collections.Frozen;
using System.Text.Json;
using DataControls.Core.Catalog;
using DataControls.Core.Definitions;

namespace DataControls.Core.Operators;

/// <summary>How many literal values an operator takes.</summary>
public enum OperandShape
{
    /// <summary>No operand ("is empty").</summary>
    None,

    /// <summary>One value ("greater than 18").</summary>
    Value,

    /// <summary>Two values, from-to ("between 5,000 and 9,000").</summary>
    Range,

    /// <summary>A list of values ("one of HR, FIN").</summary>
    List,
}

/// <summary>Everything the engine and the UI need to know about one operator.</summary>
public sealed record OperatorInfo
{
    public required ConditionOperator Operator { get; init; }

    /// <summary>The JSON key, e.g. "isEmpty".</summary>
    public string Key => JsonNamingPolicy.CamelCase.ConvertName(Operator.ToString());

    /// <summary>Hebrew label, e.g. "גדול מ".</summary>
    public required string Label { get; init; }

    /// <summary>Hebrew label when the field is a date, e.g. "אחרי" instead of "גדול מ". Null = same as <see cref="Label"/>.</summary>
    public string? DateLabel { get; init; }

    public required FrozenSet<DataType> AllowedTypes { get; init; }
    public required OperandShape Shape { get; init; }

    /// <summary>Whether the operand may be another field instead of a literal ("termination date before hire date").</summary>
    public bool AllowsFieldOperand { get; init; }

    public string LabelFor(DataType type) => type == DataType.Date && DateLabel is not null ? DateLabel : Label;

    public bool IsAllowedFor(DataType type) => AllowedTypes.Contains(type);
}

/// <summary>
/// The single source of truth for operators: which data types each one applies to, what operand it takes
/// and its Hebrew wording. Used by the validator, the describer and served to the UI (GET /api/catalog/operators),
/// so the client never duplicates these rules.
/// </summary>
public static class OperatorRegistry
{
    private static readonly FrozenSet<DataType> AllTypes = Enum.GetValues<DataType>().ToFrozenSet();
    private static readonly FrozenSet<DataType> Ordered = new[] { DataType.Integer, DataType.Decimal, DataType.Date }.ToFrozenSet();
    private static readonly FrozenSet<DataType> Listable = new[] { DataType.Text, DataType.Integer, DataType.Enum }.ToFrozenSet();
    private static readonly FrozenSet<DataType> TextOnly = new[] { DataType.Text }.ToFrozenSet();

    private static readonly FrozenDictionary<ConditionOperator, OperatorInfo> ByOperator = new OperatorInfo[]
    {
        new() { Operator = ConditionOperator.Eq,         Label = "שווה ל",             AllowedTypes = AllTypes, Shape = OperandShape.Value, AllowsFieldOperand = true },
        new() { Operator = ConditionOperator.Neq,        Label = "שונה מ",             AllowedTypes = AllTypes, Shape = OperandShape.Value, AllowsFieldOperand = true },
        new() { Operator = ConditionOperator.Gt,         Label = "גדול מ",             DateLabel = "אחרי",              AllowedTypes = Ordered, Shape = OperandShape.Value, AllowsFieldOperand = true },
        new() { Operator = ConditionOperator.Gte,        Label = "גדול או שווה ל",     DateLabel = "בתאריך או אחרי",    AllowedTypes = Ordered, Shape = OperandShape.Value, AllowsFieldOperand = true },
        new() { Operator = ConditionOperator.Lt,         Label = "קטן מ",              DateLabel = "לפני",              AllowedTypes = Ordered, Shape = OperandShape.Value, AllowsFieldOperand = true },
        new() { Operator = ConditionOperator.Lte,        Label = "קטן או שווה ל",      DateLabel = "בתאריך או לפני",    AllowedTypes = Ordered, Shape = OperandShape.Value, AllowsFieldOperand = true },
        new() { Operator = ConditionOperator.Between,    Label = "בין",                AllowedTypes = Ordered,  Shape = OperandShape.Range },
        new() { Operator = ConditionOperator.In,         Label = "אחד מ",              AllowedTypes = Listable, Shape = OperandShape.List },
        new() { Operator = ConditionOperator.Contains,   Label = "מכיל",               AllowedTypes = TextOnly, Shape = OperandShape.Value },
        new() { Operator = ConditionOperator.IsEmpty,    Label = "ריק",                AllowedTypes = AllTypes, Shape = OperandShape.None },
        new() { Operator = ConditionOperator.IsNotEmpty, Label = "לא ריק",             AllowedTypes = AllTypes, Shape = OperandShape.None },
    }.ToFrozenDictionary(o => o.Operator);

    /// <summary>All operators, in declaration order (the order shown in the UI).</summary>
    public static IReadOnlyList<OperatorInfo> All { get; } = [.. ByOperator.Values.OrderBy(o => o.Operator)];

    public static OperatorInfo Get(ConditionOperator op) =>
        ByOperator.TryGetValue(op, out var info)
            ? info
            : throw new ArgumentOutOfRangeException(nameof(op), op, "Operator is not registered.");

    /// <summary>Operators applicable to a field of the given data type (what the UI offers for that field).</summary>
    public static IReadOnlyList<OperatorInfo> ForType(DataType type) => [.. All.Where(o => o.IsAllowedFor(type))];
}
