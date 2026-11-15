using DataControls.Core.Catalog;
using DataControls.Core.Operators;

namespace DataControls.Application.Catalog;

/// <summary>The business view of the catalog sent to the UI - business names only, no tables or columns.</summary>
public sealed record EntityDto(
    string Key,
    string DisplayName,
    string DisplayNamePlural,
    string? Description,
    IReadOnlyList<FieldDto> Fields,
    IReadOnlyList<RelationDto> Relations,
    IReadOnlyList<FieldPathDto> FieldPaths);

/// <summary>
/// A field a control on the entity may use, worded by the server: "department.name" = "שם המחלקה", owned by "מחלקה" -
/// the UI lists these and never works out paths, limits or names itself. The field is <c>FieldKey</c> of <c>EntityKey</c>.
/// </summary>
public sealed record FieldPathDto(string Path, string Label, string Owner, string EntityKey, string FieldKey);

public sealed record FieldDto(
    string Key,
    string DisplayName,
    string? Description,
    DataType DataType,
    bool IsRequiredByBusiness,
    IReadOnlyList<AllowedValue> AllowedValues,
    string? Unit);

public sealed record RelationDto(string Key, string DisplayName, string? Description, string TargetEntityKey, Cardinality Cardinality);

/// <summary>The limits of a control, served so the UI never repeats them; the server still enforces each one.</summary>
public sealed record LimitsDto(
    int MaxConditions,
    int MaxDepth,
    int MaxRelationHops,
    int MaxTextValueLength,
    int MaxSearchLength,
    int MaxNameLength,
    int MaxDescriptionLength,
    int MaxAiTextLength);

public sealed record OperatorDto(
    string Key,
    string Label,
    string? DateLabel,
    IReadOnlyList<DataType> AllowedTypes,
    OperandShape Shape,
    bool AllowsFieldOperand);
