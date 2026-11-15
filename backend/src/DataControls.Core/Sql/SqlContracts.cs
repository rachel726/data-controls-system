using System.Data;
using DataControls.Core.Planning;
using DataControls.Core.Validation;

namespace DataControls.Core.Sql;

/// <summary>Turns a dialect-independent <see cref="QueryModel"/> into executable SQL for one database engine.</summary>
public interface ISqlRenderer
{
    /// <summary>One page of violations: values, reason flags and the filtered total, with search / filters / sort / paging.</summary>
    RenderedQuery RenderPage(QueryModel model, ResultQueryOptions options);

    /// <summary>Two numbers: records scanned (whole root table) and violations (records matching the control).</summary>
    RenderedQuery RenderSummary(QueryModel model, DateOnly today);
}

/// <summary>SQL text + typed parameters. The text never contains a user value.</summary>
public sealed record RenderedQuery(string Sql, IReadOnlyList<SqlParam> Parameters);

/// <summary>A typed parameter, independent of the ADO.NET provider (mapped to SqlParameter in Infrastructure).</summary>
public sealed record SqlParam(string Name, object Value, DbType DbType, int? Size = null, byte? Precision = null, byte? Scale = null);

/// <summary>What the results screen asks for. All keys must be result-column keys (whitelist).</summary>
public sealed record ResultQueryOptions
{
    /// <summary>The "today" used for computed fields such as age - a parameter, so results are deterministic and testable.</summary>
    public required DateOnly Today { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int MaxPageSize { get; init; } = 200;

    /// <summary>The configured limit of search / filter text (the caller passes ValidationLimits.MaxSearchLength).</summary>
    public int MaxSearchLength { get; init; } = ValidationLimits.Default.MaxSearchLength;
    public string? Search { get; init; }
    public string? SortKey { get; init; }
    public bool SortDescending { get; init; }
    public IReadOnlyList<ColumnFilter> Filters { get; init; } = [];
}

/// <summary>Filter one result column. Text columns match "contains"; other types match the exact value.</summary>
public sealed record ColumnFilter(string Key, string Value);

/// <summary>Invalid paging / sort / filter request. <see cref="Exception.Message"/> is user-facing Hebrew.</summary>
public sealed class QueryOptionsException(string message) : Exception(message);
