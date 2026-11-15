using DataControls.Core.Sql;
using Microsoft.Data.SqlClient;

namespace DataControls.IntegrationTests;

/// <summary>
/// The local sample database created by database/setup.ps1.
/// Override with the environment variable DATACONTROLS_TEST_DB.
/// </summary>
public static class TestDatabase
{
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DATACONTROLS_TEST_DB")
        ?? @"Server=.\SQLEXPRESS;Database=DataControlsDb;Trusted_Connection=True;TrustServerCertificate=True;Application Name=DataControls.IntegrationTests";

    /// <summary>Runs a rendered query and returns all rows as column→value dictionaries (DBNull → null).</summary>
    public static async Task<List<Dictionary<string, object?>>> QueryAsync(RenderedQuery query)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(query.Sql, connection);
        foreach (var p in query.Parameters)
        {
            var parameter = new SqlParameter(p.Name, p.DbType == System.Data.DbType.Date ? System.Data.SqlDbType.Date : default) { DbType = p.DbType, Value = p.Value };
            if (p.Size is int size) parameter.Size = size;
            if (p.Precision is byte precision) parameter.Precision = precision;
            if (p.Scale is byte scale) parameter.Scale = scale;
            command.Parameters.Add(parameter);
        }

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    public static async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
