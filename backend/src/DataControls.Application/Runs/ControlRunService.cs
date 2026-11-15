using System.Globalization;
using DataControls.Application.Common;
using DataControls.Core.Catalog;
using DataControls.Core.Controls;
using DataControls.Core.Describing;
using DataControls.Core.Planning;
using DataControls.Core.Sql;
using DataControls.Core.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Application.Runs;

public sealed class ControlRunOptions
{
    /// <summary>Bound to the "QueryExecution" configuration section.</summary>
    public const string Section = "QueryExecution";

    public int DefaultPageSize { get; set; } = 25;
    public int MaxPageSize { get; set; } = 200;
}

/// <summary>
/// Runs a saved control end to end - the pipeline the assignment asks to keep separated:
/// definition → validate (against the CURRENT catalog) → plan → render SQL → execute (read-only) → reasons.
/// <see cref="RunAsync"/> records the outcome (success with the count, or failure with the reason);
/// <see cref="GetResultsAsync"/> re-reads results for search / filter / sort / paging without recording a run.
/// </summary>
public sealed partial class ControlRunService(
    IControlRepository repository,
    ICatalogProvider catalogProvider,
    DefinitionValidator validator,
    ISqlRenderer renderer,
    IQueryExecutor executor,
    TimeProvider time,
    IOptions<ControlRunOptions> options,
    ILogger<ControlRunService> logger)
{
    public async Task<RunResultDto> RunAsync(int controlId, CancellationToken cancellationToken)
    {
        var control = await repository.GetAsync(controlId, cancellationToken) ?? throw NotFoundException.Control(controlId);
        var startedAt = time.GetUtcNow().UtcDateTime;

        try
        {
            var result = await ExecuteAsync(control, new ResultsRequest(IncludeSummary: true), cancellationToken);
            var violations = result.ViolationCount!.Value;   // a run always counts
            await repository.RecordRunAsync(controlId, new LastRun(startedAt, RunStatus.Succeeded, (int)violations, null), cancellationToken);
            LogRun(logger, controlId, violations, result.TotalScanned!.Value, result.DurationMs);
            return result;
        }
        catch (Exception ex) when (ex is QueryExecutionException or DefinitionInvalidException or QueryOptionsException)
        {
            // The failure is part of the control's history; the caller still gets the error.
            await repository.RecordRunAsync(controlId, new LastRun(startedAt, RunStatus.Failed, null, ex.Message), CancellationToken.None);
            throw;
        }
    }

    public async Task<RunResultDto> GetResultsAsync(int controlId, ResultsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var control = await repository.GetAsync(controlId, cancellationToken) ?? throw NotFoundException.Control(controlId);
        return await ExecuteAsync(control, request, cancellationToken);
    }

    private async Task<RunResultDto> ExecuteAsync(Control control, ResultsRequest request, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var catalog = await catalogProvider.GetCatalogAsync(cancellationToken);

        var validation = validator.Validate(control.Definition, catalog);
        if (!validation.IsValid)
        {
            throw new DefinitionInvalidException(validation.Errors,
                "הבקרה אינה תקינה עוד מול מבנה הנתונים הנוכחי - יש לערוך אותה לפני הרצה.");
        }

        var model = QueryPlanner.Plan(control.Definition, catalog, validator.Limits);
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var queryOptions = new ResultQueryOptions
        {
            Today = today,
            Page = request.Page,
            PageSize = request.PageSize ?? options.Value.DefaultPageSize,
            MaxPageSize = options.Value.MaxPageSize,
            MaxSearchLength = validator.Limits.MaxSearchLength,
            Search = request.Search,
            SortKey = request.SortKey,
            SortDescending = request.SortDescending,
            Filters = [.. (request.Filters ?? []).Select(f => new ColumnFilter(f.Key, f.Value))],
        };

        var pageQuery = renderer.RenderPage(model, queryOptions);   // renders (and validates options) before touching the DB
        var summary = request.IncludeSummary
            ? (await executor.QueryAsync(renderer.RenderSummary(model, today), cancellationToken)).Single()
            : null;
        var rows = await executor.QueryAsync(pageQuery, cancellationToken);

        // A page past the end (e.g. the results shrank since the user paged): serve the last page that exists,
        // so the total is always known and the screen never shows "no results" while results exist.
        if (rows.Count == 0 && queryOptions.Page > 1)
        {
            var probe = await executor.QueryAsync(renderer.RenderPage(model, queryOptions with { Page = 1, PageSize = 1 }), cancellationToken);
            var total = probe.Count > 0 ? ToLong(probe[0][SqlServerRenderer.TotalColumn]) : 0;
            queryOptions = queryOptions with { Page = (int)Math.Max(1, (total + queryOptions.PageSize - 1) / queryOptions.PageSize) };
            rows = total > 0 ? await executor.QueryAsync(renderer.RenderPage(model, queryOptions), cancellationToken) : [];
        }

        return new RunResultDto(
            control.Id,
            control.Name,
            model.RootEntity.DisplayNamePlural,
            time.GetUtcNow().UtcDateTime,
            (long)time.GetElapsedTime(started).TotalMilliseconds,
            summary is null ? null : ToLong(summary[SqlServerRenderer.ViolationsColumn]),
            summary is null ? null : ToLong(summary[SqlServerRenderer.ScannedColumn]),
            rows.Count > 0 ? ToLong(rows[0][SqlServerRenderer.TotalColumn]) : 0,
            queryOptions.Page,
            queryOptions.PageSize,
            [.. model.Columns.Select(c => new ResultColumnDto(
                c.Key, c.Field.DisplayName, c.Field.Field.DataType, c.Field.Field.Unit, c.IsConditionField, c.Field.Field.AllowedValues))],
            [.. rows.Select(r => ToRow(model, r, catalog))]);
    }

    private static ResultRowDto ToRow(QueryModel model, IReadOnlyDictionary<string, object?> row, MetadataCatalog catalog) => new(
        Convert.ToInt32(row[SqlServerRenderer.IdColumn], CultureInfo.InvariantCulture),
        HebrewFormat.Value(row.GetValueOrDefault(SqlServerRenderer.TitleColumn), model.Title.Field),
        model.Columns.ToDictionary(c => c.Key, c => HebrewFormat.Value(row.GetValueOrDefault(c.Key), c.Field.Field), StringComparer.Ordinal),
        ViolationReasonBuilder.Build(model, row, catalog));

    private static long ToLong(object? value) => Convert.ToInt64(value ?? 0, CultureInfo.InvariantCulture);

    [LoggerMessage(Level = LogLevel.Information, Message = "Control {ControlId} ran: {Violations} violations of {Scanned} records in {DurationMs} ms.")]
    private static partial void LogRun(ILogger logger, int controlId, long violations, long scanned, long durationMs);
}
