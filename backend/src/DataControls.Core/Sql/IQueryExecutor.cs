namespace DataControls.Core.Sql;

/// <summary>Executes a rendered, read-only query and returns its rows (column name → value, DB NULL → null).</summary>
public interface IQueryExecutor
{
    /// <exception cref="QueryTimeoutException">The query ran longer than the configured timeout.</exception>
    /// <exception cref="QueryPermissionException">The query tried something the read-only principal may not do.</exception>
    /// <exception cref="QueryExecutionException">Any other database error.</exception>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(RenderedQuery query, CancellationToken cancellationToken);
}

/// <summary>Running a control's query failed. <see cref="Exception.Message"/> is user-facing Hebrew; details are in the inner exception.</summary>
public class QueryExecutionException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class QueryTimeoutException(int seconds, Exception? inner = null)
    : QueryExecutionException($"הרצת הבקרה ארכה יותר מ-{seconds} שניות והופסקה. נסו לצמצם את התנאים.", inner);

public sealed class QueryPermissionException(Exception? inner = null)
    : QueryExecutionException("הבקרה ניסתה לבצע פעולה שאינה מותרת ונחסמה.", inner);
