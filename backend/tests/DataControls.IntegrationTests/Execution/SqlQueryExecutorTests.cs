using DataControls.Core.Definitions;
using DataControls.Core.Planning;
using DataControls.Core.Sql;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using DataControls.Infrastructure.Data;
using DataControls.Infrastructure.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace DataControls.IntegrationTests.Execution;

public class SqlQueryExecutorTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// A pool of exactly ONE connection: the next open is guaranteed to reuse the physical connection the executor
    /// just used, so the tests really prove that no impersonation survives into the pool.
    /// </summary>
    private static readonly string SinglePooledConnection =
        new SqlConnectionStringBuilder(TestDatabase.ConnectionString) { MaxPoolSize = 1, ApplicationName = "DataControls.ExecutorTests" }.ConnectionString;

    private static SqlQueryExecutor Executor(int timeoutSeconds = 30, int maxRows = 1000, string user = "dc_query_reader") =>
        new(new SqlConnectionFactory(SinglePooledConnection),
            Options.Create(new QueryExecutionOptions { CommandTimeoutSeconds = timeoutSeconds, MaxRows = maxRows, ReadOnlyUser = user }),
            NullLogger<SqlQueryExecutor>.Instance);

    private static async Task<string> UserOnNextPooledConnection()
    {
        await using var connection = new SqlConnection(SinglePooledConnection);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT USER_NAME();", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static RenderedQuery Raw(string sql) => new(sql, []);

    [Fact]
    public async Task Runs_a_rendered_control_query_as_the_read_only_user()
    {
        var model = QueryPlanner.Plan(DefinitionSerializer.Deserialize(SampleDefinitions.MinorsWithHighSalary), TestCatalog.Instance, ValidationLimits.Default);
        var executor = Executor();

        var page = await executor.QueryAsync(new SqlServerRenderer().RenderPage(model, new ResultQueryOptions { Today = Today }), CancellationToken.None);
        var summary = (await executor.QueryAsync(new SqlServerRenderer().RenderSummary(model, Today), CancellationToken.None)).Single();

        page.Select(r => (int)r[SqlServerRenderer.IdColumn]!).ShouldBe([201, 202, 203]);
        summary[SqlServerRenderer.ViolationsColumn].ShouldBe(3L);
        (await executor.QueryAsync(Raw("SELECT USER_NAME() AS u"), CancellationToken.None)).Single()["u"].ShouldBe("dc_query_reader");
        (await UserOnNextPooledConnection()).ShouldBe("dbo");
    }

    [Theory]
    [InlineData("DELETE FROM biz.Employees WHERE Id = -1")]
    [InlineData("UPDATE biz.Employees SET Salary = 0 WHERE Id = -1")]
    [InlineData("INSERT biz.Departments (Name, Code) VALUES (N'x', N'HACK')")]
    [InlineData("SELECT * FROM meta.Controls")]
    [InlineData("EXEC (N'DROP TABLE biz.Projects')")]
    [InlineData("EXECUTE AS USER = 'dbo'; SELECT 1")]
    public async Task Defense_in_depth_the_database_refuses_anything_but_reading_business_data(string attack)
    {
        // Even a handcrafted statement that bypassed the engine cannot change data or read the system tables.
        var ex = await Should.ThrowAsync<QueryExecutionException>(() => Executor().QueryAsync(Raw(attack), CancellationToken.None));
        ex.Message.Any(c => c is >= 'א' and <= 'ת').ShouldBeTrue();

        (await UserOnNextPooledConnection()).ShouldBe("dbo");
        (await TestDatabase.ScalarAsync("SELECT COUNT_BIG(*) FROM biz.Projects")).ShouldBe(20L);
        (await TestDatabase.ScalarAsync("SELECT COUNT_BIG(*) FROM biz.Departments WHERE Code = N'HACK'")).ShouldBe(0L);
    }

    [Fact]
    public async Task Permission_errors_are_reported_as_such() =>
        await Should.ThrowAsync<QueryPermissionException>(() =>
            Executor().QueryAsync(Raw("DELETE FROM biz.Employees WHERE Id = -1"), CancellationToken.None));

    [Fact]
    public async Task A_timeout_is_reported_and_does_not_leave_the_pooled_connection_impersonated()
    {
        var ex = await Should.ThrowAsync<QueryTimeoutException>(() =>
            Executor(timeoutSeconds: 1).QueryAsync(Raw("WAITFOR DELAY '00:00:05'; SELECT 1 AS x"), CancellationToken.None));

        ex.Message.ShouldContain("1 שניות");
        (await UserOnNextPooledConnection()).ShouldBe("dbo");   // REVERT never ran - the pool reset the session
    }

    [Fact]
    public async Task Other_database_errors_become_a_generic_Hebrew_error_without_internals()
    {
        var ex = await Should.ThrowAsync<QueryExecutionException>(() =>
            Executor().QueryAsync(Raw("SELECT 1 / 0 AS x"), CancellationToken.None));

        ex.ShouldNotBeOfType<QueryPermissionException>();
        ex.Message.ShouldNotContain("divide");
        ex.InnerException.ShouldBeOfType<SqlException>();
        (await UserOnNextPooledConnection()).ShouldBe("dbo");
    }

    [Fact]
    public async Task An_error_after_rows_were_streamed_is_not_swallowed()
    {
        // Regression: the CATCH block's THROW arrives after the result set; it must not look like "no violations".
        await Should.ThrowAsync<QueryExecutionException>(() =>
            Executor().QueryAsync(Raw("SELECT Id, 1 / (Id - 5) AS x FROM biz.Employees ORDER BY Id"), CancellationToken.None));
    }

    [Fact]
    public async Task A_batch_aborting_error_does_not_leave_the_pooled_connection_impersonated()
    {
        // "Invalid object name" at the same scope is NOT caught by TRY/CATCH - REVERT is skipped.
        await Should.ThrowAsync<QueryExecutionException>(() =>
            Executor().QueryAsync(Raw("SELECT * FROM biz.NoSuchTable"), CancellationToken.None));

        (await UserOnNextPooledConnection()).ShouldBe("dbo");
    }

    [Fact]
    public async Task Exceeding_the_row_cap_is_an_error_not_a_silently_truncated_result()
    {
        var ex = await Should.ThrowAsync<QueryExecutionException>(() =>
            Executor(maxRows: 5).QueryAsync(Raw("SELECT Id FROM biz.Employees"), CancellationToken.None));

        ex.Message.ShouldContain("5");
        (await UserOnNextPooledConnection()).ShouldBe("dbo");
        (await Executor(maxRows: 5).QueryAsync(Raw("SELECT TOP 5 Id FROM biz.Employees"), CancellationToken.None)).Count.ShouldBe(5);
    }

    [Fact]
    public async Task Returns_nulls_and_typed_values()
    {
        var row = (await Executor().QueryAsync(new RenderedQuery("SELECT CAST(NULL AS INT) AS n, @p0 AS d, @p1 AS t",
            [new SqlParam("@p0", new DateOnly(2020, 1, 5), System.Data.DbType.Date), new SqlParam("@p1", "שלום", System.Data.DbType.String, 4000)]),
            CancellationToken.None)).Single();

        row["n"].ShouldBeNull();
        row["d"].ShouldBe(new DateTime(2020, 1, 5));
        row["t"].ShouldBe("שלום");
    }

    [Theory]
    [InlineData("dc_query_reader; DROP TABLE x")]
    [InlineData("reader'--")]
    [InlineData("")]
    public void Rejects_an_invalid_configured_user_name(string user) =>
        Should.Throw<InvalidOperationException>(() => Executor(user: user));
}
