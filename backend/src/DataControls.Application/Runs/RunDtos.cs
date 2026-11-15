using DataControls.Core.Catalog;

namespace DataControls.Application.Runs;

/// <summary>
/// What the results screen asks for. Keys are result-column keys. <see cref="IncludeSummary"/> = false skips counting
/// the control's violations and the records checked - the numbers do not change while the user pages, sorts,
/// filters or searches, so the screen keeps the ones it has.
/// </summary>
public sealed record ResultsRequest(
    int Page = 1,
    int? PageSize = null,
    string? Search = null,
    string? SortKey = null,
    bool SortDescending = false,
    IReadOnlyList<ColumnFilterDto>? Filters = null,
    bool IncludeSummary = true);

public sealed record ColumnFilterDto(string Key, string Value);

/// <summary>A page of violations of one control, with summary numbers and a reason per record.</summary>
public sealed record RunResultDto(
    int ControlId,
    string ControlName,
    string RootEntityName,
    DateTime ExecutedAt,
    long DurationMs,
    long? ViolationCount,
    long? TotalScanned,
    long TotalFiltered,
    int Page,
    int PageSize,
    IReadOnlyList<ResultColumnDto> Columns,
    IReadOnlyList<ResultRowDto> Rows);

/// <summary>
/// A result column in business terms. <see cref="IsConditionField"/> highlights fields used by the control;
/// <see cref="AllowedValues"/> lets the UI filter an enum column by business label.
/// </summary>
public sealed record ResultColumnDto(
    string Key,
    string DisplayName,
    DataType DataType,
    string? Unit,
    bool IsConditionField,
    IReadOnlyList<AllowedValue> AllowedValues);

/// <summary>One violating record: its id, title, values already formatted in Hebrew, and why it violates the control.</summary>
public sealed record ResultRowDto(int RecordId, string Title, IReadOnlyDictionary<string, string> Values, IReadOnlyList<string> Reasons);
