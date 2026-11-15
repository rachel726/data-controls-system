using DataControls.Core.Catalog;
using DataControls.Core.Controls;
using DataControls.Core.Sql;
using DataControls.Core.Tests.Fixtures;

namespace DataControls.Application.Tests;

internal static class TestJson
{
    /// <summary>A definition that references a field the catalog does not have (e.g. saved before the catalog changed).</summary>
    public const string UnknownField = """
        { "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "shoeSize", "operator": "isEmpty" } ] } }
        """;

    /// <summary>"contains" on a number - a type mismatch.</summary>
    public const string ContainsOnAge = """
        { "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "age", "operator": "contains", "operand": { "kind": "literal", "value": "x" } } ] } }
        """;
}

internal sealed class FakeCatalogProvider(MetadataCatalog? catalog = null) : ICatalogProvider
{
    public Task<MetadataCatalog> GetCatalogAsync(CancellationToken cancellationToken) => Task.FromResult(catalog ?? TestCatalog.Instance);
}

/// <summary>In-memory repository with the same observable behaviour as the real one.</summary>
internal sealed class FakeControlRepository : IControlRepository
{
    private readonly Dictionary<int, Control> _controls = [];
    private int _nextId = 1;

    public List<(int Id, LastRun Run)> RecordedRuns { get; } = [];
    public int Writes { get; private set; }

    public Control Seed(ControlData data)
    {
        var id = CreateAsync(data, CancellationToken.None).Result;
        return _controls[id];
    }

    public Task<IReadOnlyList<ControlSummary>> ListAsync(string? search, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ControlSummary>>([.. _controls.Values
            .Where(c => search is null || c.Name.Contains(search, StringComparison.Ordinal))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new ControlSummary(c.Id, c.Name, c.Description, c.Definition.RootEntity, c.UpdatedAt, c.LastRun))]);

    public Task<Control?> GetAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(_controls.GetValueOrDefault(id));

    public Task<int> CreateAsync(ControlData data, CancellationToken cancellationToken)
    {
        if (_controls.Values.Any(c => c.Name == data.Name.Trim()))
        {
            throw new DuplicateControlNameException(data.Name);
        }

        Writes++;
        var now = new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc);
        var id = _nextId++;
        _controls[id] = new Control(id, data.Name.Trim(), data.Description, data.Definition, now, now, null);
        return Task.FromResult(id);
    }

    public Task<bool> UpdateAsync(int id, ControlData data, CancellationToken cancellationToken)
    {
        if (!_controls.TryGetValue(id, out var existing))
        {
            return Task.FromResult(false);
        }

        Writes++;
        _controls[id] = existing with { Name = data.Name.Trim(), Description = data.Description, Definition = data.Definition, LastRun = null };
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken) => Task.FromResult(_controls.Remove(id));

    public Task<bool> RecordRunAsync(int id, LastRun run, CancellationToken cancellationToken)
    {
        RecordedRuns.Add((id, run));
        if (_controls.TryGetValue(id, out var c))
        {
            _controls[id] = c with { LastRun = run };
        }

        return Task.FromResult(true);
    }
}

/// <summary>Returns canned rows: one summary row for the summary query, the given page rows for the page query.</summary>
internal sealed class FakeQueryExecutor : IQueryExecutor
{
    public long Scanned { get; set; } = 222;
    public long Violations { get; set; } = 3;
    public List<Dictionary<string, object?>> PageRows { get; set; } = [];
    public Exception? Throw { get; set; }
    public List<RenderedQuery> Queries { get; } = [];

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(RenderedQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        if (Throw is not null)
        {
            throw Throw;
        }

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = query.Sql.StartsWith("SELECT (SELECT COUNT_BIG", StringComparison.Ordinal)
            ? [new Dictionary<string, object?> { [SqlServerRenderer.ScannedColumn] = Scanned, [SqlServerRenderer.ViolationsColumn] = Violations }]
            : [.. PageRows];
        return Task.FromResult(rows);
    }
}

internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
