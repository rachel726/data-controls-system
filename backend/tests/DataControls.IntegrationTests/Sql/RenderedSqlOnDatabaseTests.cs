using DataControls.Core.Definitions;
using DataControls.Core.Planning;
using DataControls.Core.Sql;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Shouldly;

namespace DataControls.IntegrationTests.Sql;

/// <summary>
/// Runs the SQL produced by the engine against the real sample database (database/setup.ps1)
/// and checks it against the planted violations - proves the SQL is valid AND correct, not just well-formed.
/// </summary>
public class RenderedSqlOnDatabaseTests
{
    private static readonly SqlServerRenderer Renderer = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);

    private static QueryModel Plan(string json)
    {
        var definition = DefinitionSerializer.Deserialize(json);
        new DefinitionValidator().Validate(definition, TestCatalog.Instance).IsValid.ShouldBeTrue();
        return QueryPlanner.Plan(definition, TestCatalog.Instance, ValidationLimits.Default);
    }

    private static string Def(string root, string childrenJson) =>
        $$"""{ "schemaVersion": 1, "rootEntity": "{{root}}", "filter": { "type": "group", "logic": "and", "children": [ {{childrenJson}} ] } }""";

    public static TheoryData<string, string, long, long> Samples => new()
    {
        { nameof(SampleDefinitions.MinorsWithHighSalary), SampleDefinitions.MinorsWithHighSalary, 3, 222 },
        { nameof(SampleDefinitions.TerminationBeforeHire), SampleDefinitions.TerminationBeforeHire, 4, 222 },
        { nameof(SampleDefinitions.ActiveInInactiveDepartment), SampleDefinitions.ActiveInInactiveDepartment, 5, 222 },
        { nameof(SampleDefinitions.ActiveMissingRequiredData), SampleDefinitions.ActiveMissingRequiredData, 6, 222 },
        { nameof(SampleDefinitions.EarnsMoreThanManager), SampleDefinitions.EarnsMoreThanManager, 3, 222 },
        { nameof(SampleDefinitions.ProjectEndBeforeStart), SampleDefinitions.ProjectEndBeforeStart, 2, 20 },
        { nameof(SampleDefinitions.ActiveDepartmentWithoutActiveEmployees), SampleDefinitions.ActiveDepartmentWithoutActiveEmployees, 1, 10 },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Sample_control_finds_exactly_the_planted_violations(string name, string json, long expectedViolations, long expectedScanned)
    {
        var model = Plan(json);

        var summary = (await TestDatabase.QueryAsync(Renderer.RenderSummary(model, Today))).Single();
        summary[SqlServerRenderer.ViolationsColumn].ShouldBe(expectedViolations, name);
        summary[SqlServerRenderer.ScannedColumn].ShouldBe(expectedScanned, name);

        var page = await TestDatabase.QueryAsync(Renderer.RenderPage(model, new ResultQueryOptions { Today = Today, PageSize = 50 }));
        page.Count.ShouldBe((int)expectedViolations, name);
        page.ShouldAllBe(r => (int)r[SqlServerRenderer.TotalColumn]! == expectedViolations);

        // Every violating row met the control: in an AND root, every top-level flag is 1.
        var topLevelFlags = model.Flags.Where(f => f.NodePath.Count(c => c == '.') == 1).Select(f => SqlServerRenderer.FlagColumn(f.Index)).ToList();
        if (model.Filter.Logic == LogicalOperator.And)
        {
            page.ShouldAllBe(r => topLevelFlags.All(flag => (int)r[flag]! == 1));
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Every_violation_on_real_data_gets_a_Hebrew_reason(string name, string json, long expectedViolations, long scanned)
    {
        _ = scanned;
        var model = Plan(json);
        var page = await TestDatabase.QueryAsync(Renderer.RenderPage(model, new ResultQueryOptions { Today = Today, PageSize = 50 }));

        page.Count.ShouldBe((int)expectedViolations, name);
        foreach (var row in page)
        {
            var reasons = DataControls.Core.Describing.ViolationReasonBuilder.Build(model, row, TestCatalog.Instance);
            reasons.ShouldNotBeEmpty(name);
            reasons.ShouldAllBe(r => r.Any(c => c >= 'א' && c <= 'ת'));
        }
    }

    [Fact]
    public async Task Real_reasons_contain_the_records_actual_values()
    {
        var model = Plan(SampleDefinitions.EarnsMoreThanManager);
        var page = await TestDatabase.QueryAsync(Renderer.RenderPage(model, new ResultQueryOptions { Today = Today, PageSize = 50 }));

        var reason = DataControls.Core.Describing.ViolationReasonBuilder.Build(model, page.Single(r => (int)r[SqlServerRenderer.IdColumn]! == 220), TestCatalog.Instance)
            .ShouldHaveSingleItem();
        reason.ShouldBe("שכר חודשי (13,000 ₪) גדול משכר חודשי של מנהל ישיר (12,000 ₪)");
    }

    [Fact]
    public async Task Or_group_flags_tell_which_branch_fired()
    {
        var page = await TestDatabase.QueryAsync(Renderer.RenderPage(Plan(SampleDefinitions.ActiveMissingRequiredData),
            new ResultQueryOptions { Today = Today, PageSize = 50 }));

        // flags: 0 = active, 1 = email empty, 2 = phone empty, 3 = department.name empty
        var byId = page.ToDictionary(r => (int)r[SqlServerRenderer.IdColumn]!);
        ((int)byId[213]["__c1"]!, (int)byId[213]["__c2"]!, (int)byId[213]["__c3"]!).ShouldBe((1, 0, 0));  // email NULL
        ((int)byId[214]["__c1"]!).ShouldBe(1);                                                             // email '' (blank)
        ((int)byId[216]["__c2"]!).ShouldBe(1);                                                             // phone '   '
        ((int)byId[217]["__c3"]!).ShouldBe(1);                                                             // no department
    }

    [Theory]
    [InlineData("gte", 18)]
    [InlineData("gt", 50)]
    [InlineData("lt", 30)]
    [InlineData("lte", 40)]
    [InlineData("eq", 45)]
    [InlineData("neq", 45)]
    public async Task Sargable_age_rewrite_matches_the_exact_age_expression_on_real_data(string op, int years)
    {
        var json = Def("employee", $$"""{ "type": "condition", "field": "age", "operator": "{{op}}", "operand": { "kind": "literal", "value": {{years}} } }""");
        var summary = (await TestDatabase.QueryAsync(Renderer.RenderSummary(Plan(json), Today))).Single();

        var sqlOp = op switch { "gte" => ">=", "gt" => ">", "lt" => "<", "lte" => "<=", "eq" => "=", _ => "<>" };
        var nullClause = op == "neq" ? " OR BirthDate IS NULL" : "";
        var reference = await TestDatabase.ScalarAsync($"""
            DECLARE @t DATE = CAST(GETDATE() AS DATE);
            SELECT COUNT_BIG(*) FROM biz.Employees
            WHERE (DATEDIFF(YEAR, BirthDate, @t) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, BirthDate, @t), BirthDate) > @t THEN 1 ELSE 0 END) {sqlOp} {years}{nullClause}
            """);

        summary[SqlServerRenderer.ViolationsColumn].ShouldBe(reference);
    }

    // "Not equal to" a value: an empty value counts as different (docs/ASSUMPTIONS-LIMITATIONS.md).
    // The reference is written independently with IS DISTINCT FROM; the data has NULLs in both columns.
    [Theory]
    [InlineData("email", "\"someone@example.com\"", "Email IS DISTINCT FROM N'someone@example.com'")]
    [InlineData("terminationDate", "\"2020-01-01\"", "TerminationDate IS DISTINCT FROM '2020-01-01'")]
    public async Task Not_equal_to_a_value_counts_an_empty_value_as_different(string field, string valueJson, string reference)
    {
        var json = Def("employee", $$"""{ "type": "condition", "field": "{{field}}", "operator": "neq", "operand": { "kind": "literal", "value": {{valueJson}} } }""");
        var summary = (await TestDatabase.QueryAsync(Renderer.RenderSummary(Plan(json), Today))).Single();

        var expected = await TestDatabase.ScalarAsync($"SELECT COUNT_BIG(*) FROM biz.Employees WHERE {reference}");
        summary[SqlServerRenderer.ViolationsColumn].ShouldBe(expected);
    }

    // "Not equal to" another field: different when exactly one side is empty; two empty values are not different.
    // Together the two pairs cover every combination present in the data: left empty, right empty, both empty, both set.
    [Theory]
    [InlineData("manager.terminationDate", "LEFT JOIN biz.Employees m ON m.Id = e.ManagerId", "m.TerminationDate")]
    [InlineData("hireDate", "", "e.HireDate")]
    public async Task Not_equal_to_another_field_treats_one_empty_side_as_different(string otherField, string join, string otherColumn)
    {
        var json = Def("employee", $$"""{ "type": "condition", "field": "terminationDate", "operator": "neq", "operand": { "kind": "field", "field": "{{otherField}}" } }""");
        var summary = (await TestDatabase.QueryAsync(Renderer.RenderSummary(Plan(json), Today))).Single();

        var expected = await TestDatabase.ScalarAsync(
            $"SELECT COUNT_BIG(*) FROM biz.Employees e {join} WHERE e.TerminationDate IS DISTINCT FROM {otherColumn}");
        summary[SqlServerRenderer.ViolationsColumn].ShouldBe(expected);
    }

    [Fact]
    public async Task Csharp_age_twin_matches_the_SQL_age_expression_on_leap_day_edges()
    {
        // The equivalence proof in YearsSinceCutoffTests relies on YearsSinceCutoff.YearsSince == the SQL display expression.
        var pairs = new List<(DateOnly Birth, DateOnly Today)>();
        DateOnly[] todays = [new(2026, 2, 27), new(2026, 2, 28), new(2026, 3, 1), new(2028, 2, 28), new(2028, 2, 29), new(2028, 3, 1)];
        DateOnly[] births = [new(2008, 2, 28), new(2008, 2, 29), new(2008, 3, 1), new(2010, 2, 28), new(2010, 3, 1), new(2012, 2, 29), new(2000, 12, 31)];
        foreach (var t in todays)
        {
            pairs.AddRange(births.Select(b => (b, t)));
        }

        var values = string.Join(", ", pairs.Select(p => $"('{p.Birth:yyyy-MM-dd}', '{p.Today:yyyy-MM-dd}')"));
        var rows = await TestDatabase.QueryAsync(new RenderedQuery($"""
            SELECT b, t, DATEDIFF(YEAR, b, t) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, b, t), b) > t THEN 1 ELSE 0 END AS age
            FROM (VALUES {values}) AS x(bs, ts) CROSS APPLY (SELECT CAST(bs AS DATE) AS b, CAST(ts AS DATE) AS t) AS c
            """, []));

        rows.Count.ShouldBe(pairs.Count);
        foreach (var row in rows)
        {
            var birth = DateOnly.FromDateTime((DateTime)row["b"]!);
            var today = DateOnly.FromDateTime((DateTime)row["t"]!);
            ((int)row["age"]!).ShouldBe(YearsSinceCutoff.YearsSince(birth, today), $"born {birth}, today {today}");
        }
    }

    [Fact]
    public async Task Result_values_are_typed_and_age_is_computed()
    {
        var page = await TestDatabase.QueryAsync(Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary),
            new ResultQueryOptions { Today = Today, PageSize = 50 }));

        page.Select(r => (int)r[SqlServerRenderer.IdColumn]!).ShouldBe([201, 202, 203]);
        page.ShouldAllBe(r => (int)r["age"]! < 18 && (decimal)r["salary"]! > 15000m);
        page.ShouldAllBe(r => !string.IsNullOrWhiteSpace((string)r[SqlServerRenderer.TitleColumn]!));
    }

    [Fact]
    public async Task Search_filters_sort_and_paging_work_on_the_database()
    {
        var model = Plan(Def("employee", """{ "type": "condition", "field": "isActive", "operator": "eq", "operand": { "kind": "literal", "value": true } }"""));
        var all = (await TestDatabase.QueryAsync(Renderer.RenderSummary(model, Today))).Single()[SqlServerRenderer.ViolationsColumn];

        // paging: page 2 of size 10, sorted by salary descending
        var page2 = await TestDatabase.QueryAsync(Renderer.RenderPage(model,
            new ResultQueryOptions { Today = Today, Page = 2, PageSize = 10, SortKey = "employeeNumber", SortDescending = true }));
        page2.Count.ShouldBe(10);
        ((int)page2[0][SqlServerRenderer.TotalColumn]!).ShouldBe((int)(long)all!);
        page2.Select(r => (string)r["employeeNumber"]!).ShouldBeInOrder(SortDirection.Descending);

        // search in Hebrew (title = full name) - every returned row contains the text
        var search = await TestDatabase.QueryAsync(Renderer.RenderPage(model, new ResultQueryOptions { Today = Today, PageSize = 200, Search = "כהן" }));
        search.ShouldNotBeEmpty();
        search.ShouldAllBe(r => ((string)r[SqlServerRenderer.TitleColumn]!).Contains("כהן"));

        // column filter on a text column + a LIKE wildcard typed by the user is matched literally (no rows)
        var filtered = await TestDatabase.QueryAsync(Renderer.RenderPage(model,
            new ResultQueryOptions { Today = Today, PageSize = 200, Filters = [new ColumnFilter("employeeNumber", "E1010")] }));
        filtered.ShouldAllBe(r => ((string)r["employeeNumber"]!).Contains("E1010"));
        (await TestDatabase.QueryAsync(Renderer.RenderPage(model,
            new ResultQueryOptions { Today = Today, Filters = [new ColumnFilter("employeeNumber", "%")] }))).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_malicious_value_is_just_data()
    {
        var model = Plan(Def("employee", """{ "type": "condition", "field": "fullName", "operator": "contains", "operand": { "kind": "literal", "value": "'; DROP TABLE biz.Projects; --" } }"""));

        var summary = (await TestDatabase.QueryAsync(Renderer.RenderSummary(model, Today))).Single();

        summary[SqlServerRenderer.ViolationsColumn].ShouldBe(0L);
        (await TestDatabase.ScalarAsync("SELECT COUNT_BIG(*) FROM biz.Projects")).ShouldBe(20L);
    }
}
