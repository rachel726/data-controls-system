using Dapper;
using DataControls.Core.Controls;
using DataControls.Core.Definitions;
using DataControls.Infrastructure.Data;
using Microsoft.Data.SqlClient;

namespace DataControls.Infrastructure.Controls;

/// <summary>Dapper implementation of <see cref="IControlRepository"/> over meta.Controls.</summary>
public sealed class ControlRepository(ISqlConnectionFactory connections) : IControlRepository
{
    /// <summary>meta.Controls.LastRunError is NVARCHAR(1000).</summary>
    public const int MaxErrorLength = 1000;

    // Unique index violation: 2601 (unique index), 2627 (unique constraint).
    private static readonly int[] UniqueViolation = [2601, 2627];

    private const string SummaryColumns = """
        Id, Name, Description, RootEntityKey, UpdatedAt, LastRunAt, LastRunStatus, LastViolationCount, LastRunError
        """;

    public async Task<IReadOnlyList<ControlSummary>> ListAsync(string? search, CancellationToken cancellationToken)
    {
        var term = search?.Trim();
        var sql = $"""
            SELECT {SummaryColumns}
            FROM meta.Controls
            WHERE IsActive = 1
              AND (@pattern IS NULL OR Name LIKE @pattern ESCAPE N'\' OR Description LIKE @pattern ESCAPE N'\')
            ORDER BY Name;
            """;

        await using var connection = connections.Create();
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(sql,
            new { pattern = Text(string.IsNullOrEmpty(term) ? null : LikeContains(term)) }, cancellationToken: cancellationToken));

        return [.. rows.Select(r => new ControlSummary(r.Id, r.Name, r.Description, r.RootEntityKey, Utc(r.UpdatedAt), ToLastRun(r)))];
    }

    public async Task<Control?> GetAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Description, DefinitionJson, CreatedAt, UpdatedAt, LastRunAt, LastRunStatus, LastViolationCount, LastRunError
            FROM meta.Controls
            WHERE Id = @id AND IsActive = 1;
            """;

        await using var connection = connections.Create();
        var row = await connection.QuerySingleOrDefaultAsync<ControlRow>(new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new Control(row.Id, row.Name, row.Description, DefinitionSerializer.Deserialize(row.DefinitionJson),
                Utc(row.CreatedAt), Utc(row.UpdatedAt), ToLastRun(row));
    }

    public async Task<int> CreateAsync(ControlData data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        const string sql = """
            INSERT meta.Controls (Name, Description, RootEntityKey, DefinitionJson)
            OUTPUT INSERTED.Id
            VALUES (@name, @description, @root, @json);
            """;

        return await WithDuplicateNameCheck(data.Name, async connection =>
            await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, Parameters(data), cancellationToken: cancellationToken)));
    }

    public async Task<bool> UpdateAsync(int id, ControlData data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        const string sql = """
            UPDATE meta.Controls
            SET Name = @name, Description = @description, RootEntityKey = @root, DefinitionJson = @json,
                UpdatedAt = SYSUTCDATETIME(),
                LastRunAt = NULL, LastRunStatus = NULL, LastViolationCount = NULL, LastRunError = NULL
            WHERE Id = @id AND IsActive = 1;
            """;

        var parameters = new DynamicParameters(Parameters(data));
        parameters.Add("id", id);

        return await WithDuplicateNameCheck(data.Name, async connection =>
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)) > 0);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE meta.Controls SET IsActive = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id AND IsActive = 1;";

        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken)) > 0;
    }

    public async Task<bool> RecordRunAsync(int id, LastRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        const string sql = """
            UPDATE meta.Controls
            SET LastRunAt = @runAt, LastRunStatus = @status, LastViolationCount = @violations, LastRunError = @error
            WHERE Id = @id AND IsActive = 1;
            """;

        await using var connection = connections.Create();
        return await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            id,
            runAt = run.RunAt,
            status = run.Status.ToString(),
            violations = run.ViolationCount,
            error = Text(run.Error is { Length: > MaxErrorLength } e ? e[..MaxErrorLength] : run.Error),
        }, cancellationToken: cancellationToken)) > 0;
    }

    // ---------- helpers ----------

    private async Task<T> WithDuplicateNameCheck<T>(string name, Func<SqlConnection, Task<T>> action)
    {
        await using var connection = connections.Create();
        try
        {
            return await action(connection);
        }
        catch (SqlException ex) when (UniqueViolation.Contains(ex.Number))
        {
            throw new DuplicateControlNameException(name);
        }
    }

    private static object Parameters(ControlData data) => new
    {
        name = Text(data.Name.Trim()),
        description = Text(string.IsNullOrWhiteSpace(data.Description) ? null : data.Description.Trim()),
        root = new DbString { Value = data.Definition.RootEntity, IsAnsi = true, Length = 50 },
        json = new DbString { Value = DefinitionSerializer.Serialize(data.Definition), IsAnsi = false, Length = -1 },
    };

    /// <summary>NVARCHAR(4000) parameter: one cached plan regardless of value length.</summary>
    private static DbString Text(string? value) => new() { Value = value, IsAnsi = false, Length = 4000 };

    private static string LikeContains(string value) =>
        "%" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("%", "\\%", StringComparison.Ordinal)
                   .Replace("_", "\\_", StringComparison.Ordinal)
                   .Replace("[", "\\[", StringComparison.Ordinal) + "%";

    private static LastRun? ToLastRun(ILastRunColumns row) =>
        row.LastRunAt is null || row.LastRunStatus is null
            ? null
            : new LastRun(Utc(row.LastRunAt.Value), Enum.Parse<RunStatus>(row.LastRunStatus), row.LastViolationCount, row.LastRunError);

    /// <summary>
    /// All timestamps are stored in UTC (SYSUTCDATETIME / TimeProvider.GetUtcNow), but DATETIME2 carries no time zone,
    /// so the driver returns them as Unspecified. Marking them UTC makes the API emit "...Z" and the UI show local time.
    /// </summary>
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private interface ILastRunColumns
    {
        DateTime? LastRunAt { get; }
        string? LastRunStatus { get; }
        int? LastViolationCount { get; }
        string? LastRunError { get; }
    }

    private sealed record SummaryRow(int Id, string Name, string? Description, string RootEntityKey, DateTime UpdatedAt,
        DateTime? LastRunAt, string? LastRunStatus, int? LastViolationCount, string? LastRunError) : ILastRunColumns;

    private sealed record ControlRow(int Id, string Name, string? Description, string DefinitionJson, DateTime CreatedAt, DateTime UpdatedAt,
        DateTime? LastRunAt, string? LastRunStatus, int? LastViolationCount, string? LastRunError) : ILastRunColumns;
}
