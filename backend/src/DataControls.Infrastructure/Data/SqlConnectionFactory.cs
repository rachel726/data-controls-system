using Microsoft.Data.SqlClient;

namespace DataControls.Infrastructure.Data;

/// <summary>Creates connections to the application database (the app's own identity: reads/writes "meta").</summary>
public interface ISqlConnectionFactory
{
    SqlConnection Create();
}

public sealed class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DataControlsDb' is missing (appsettings.json → ConnectionStrings).");
        }

        _connectionString = connectionString;
    }

    public SqlConnection Create() => new(_connectionString);
}
