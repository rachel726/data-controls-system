using System.Data;
using System.Text.RegularExpressions;
using DataControls.Core.Sql;
using DataControls.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Execution;

public sealed class QueryExecutionOptions
{
    public const string Section = "QueryExecution";

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Database principal with SELECT on the business schema only (database/scripts/03_security.sql).</summary>
    public string ReadOnlyUser { get; set; } = "dc_query_reader";

    /// <summary>Hard cap on rows read from one query (pages are much smaller; this is a safety net).</summary>
    public int MaxRows { get; set; } = 1000;
}

/// <summary>
/// Runs every generated query inside the security context of a read-only database user:
/// <code>
/// EXECUTE AS USER = N'dc_query_reader';
/// BEGIN TRY  &lt;query&gt;  REVERT;  END TRY
/// BEGIN CATCH  REVERT;  THROW;  END CATCH
/// </code>
/// Even if the engine had a bug, the database itself refuses to modify data or read the system's own tables.
/// (If a timeout aborts the batch before REVERT, the pooled connection is reset on its next use - proven by
/// the integration tests.)
/// </summary>
public sealed partial class SqlQueryExecutor : IQueryExecutor
{
    // SQL Server error numbers: permission denied on object / schema / database, and "cannot execute as".
    private static readonly int[] PermissionErrors = [229, 230, 262, 300, 15247, 15517, 15406];
    private const int TimeoutError = -2;

    private readonly ISqlConnectionFactory _connections;
    private readonly QueryExecutionOptions _options;
    private readonly ILogger<SqlQueryExecutor> _logger;

    public SqlQueryExecutor(ISqlConnectionFactory connections, IOptions<QueryExecutionOptions> options, ILogger<SqlQueryExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connections = connections;
        _options = options.Value;
        _logger = logger;

        if (!PrincipalName().IsMatch(_options.ReadOnlyUser ?? string.Empty))
        {
            throw new InvalidOperationException($"QueryExecution:ReadOnlyUser '{_options.ReadOnlyUser}' is not a valid database user name.");
        }

        if (_options.CommandTimeoutSeconds <= 0 || _options.MaxRows <= 0)
        {
            throw new InvalidOperationException("QueryExecution:CommandTimeoutSeconds and MaxRows must be positive.");
        }
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(RenderedQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The leading ';' lets the query start with a CTE ("WITH …") right after BEGIN TRY.
        var batch = $"""
            EXECUTE AS USER = N'{_options.ReadOnlyUser}';
            BEGIN TRY
            ;{query.Sql}
            REVERT;
            END TRY
            BEGIN CATCH
            REVERT;
            THROW;
            END CATCH
            """;

        LogQuery(_logger, query.Sql);   // SQL text only - parameter values may contain personal data

        await using var connection = _connections.Create();
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = _options.CommandTimeoutSeconds };
            command.Parameters.AddRange([.. query.Parameters.Select(ToSqlParameter)]);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = await ReadRowsAsync(reader, cancellationToken);
            if (rows is null)
            {
                // Safety net only (pages are far smaller). Truncated data must never look like a real result.
                LogRowCapReached(_logger, _options.MaxRows);
                command.Cancel();
                SqlConnection.ClearPool(connection);
                throw new QueryExecutionException($"התוצאה גדולה מהמותר (יותר מ-{_options.MaxRows} שורות).");
            }

            // An error raised by the CATCH block (THROW) arrives AFTER the query's result set.
            // Draining the remaining results makes it surface - otherwise a failed query would look like "no rows".
            while (await reader.NextResultAsync(cancellationToken))
            {
            }

            return rows;
        }
        catch (SqlException ex)
        {
            // Never return a possibly tainted session to the pool: after a timeout the session can be in the
            // "kill" state, and a batch-aborting error may skip REVERT. Errors are rare, so this costs nothing.
            SqlConnection.ClearPool(connection);

            if (ex.Number == TimeoutError)
            {
                throw new QueryTimeoutException(_options.CommandTimeoutSeconds, ex);
            }

            if (PermissionErrors.Contains(ex.Number))
            {
                LogPermissionDenied(_logger, ex.Number, ex.Message);
                throw new QueryPermissionException(ex);
            }

            LogFailed(_logger, ex.Number, ex);
            throw new QueryExecutionException("אירעה שגיאה בהרצת הבקרה. אם התקלה חוזרת, פנו לתמיכה.", ex);
        }
    }

    /// <returns>The rows, or null if there are more than <see cref="QueryExecutionOptions.MaxRows"/>.</returns>
    private async Task<List<IReadOnlyDictionary<string, object?>>?> ReadRowsAsync(SqlDataReader reader, CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == _options.MaxRows)
            {
                return null;
            }

            var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static SqlParameter ToSqlParameter(SqlParam p)
    {
        var parameter = new SqlParameter
        {
            ParameterName = p.Name,
            SqlDbType = p.DbType switch
            {
                DbType.String => SqlDbType.NVarChar,
                DbType.Int64 => SqlDbType.BigInt,
                DbType.Decimal => SqlDbType.Decimal,
                DbType.Date => SqlDbType.Date,
                DbType.Boolean => SqlDbType.Bit,
                _ => throw new NotSupportedException($"Parameter type {p.DbType}"),
            },
            Value = p.Value,
        };

        if (p.Size is int size) parameter.Size = size;
        if (p.Precision is byte precision) parameter.Precision = precision;
        if (p.Scale is byte scale) parameter.Scale = scale;
        return parameter;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex PrincipalName();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Executing control query:\n{Sql}")]
    private static partial void LogQuery(ILogger logger, string sql);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query exceeded the row cap of {MaxRows} and was cancelled.")]
    private static partial void LogRowCapReached(ILogger logger, int maxRows);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query blocked by read-only permissions (SQL error {Number}): {Message}")]
    private static partial void LogPermissionDenied(ILogger logger, int number, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Control query failed (SQL error {Number}).")]
    private static partial void LogFailed(ILogger logger, int number, Exception exception);
}
