using System.Data;
using System.Text.RegularExpressions;
using DataControls.Core.Catalog;
using DataControls.Core.Definitions;
using DataControls.Core.Planning;
using DataControls.Core.Sql;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Shouldly;

namespace DataControls.Core.Tests.Sql;

public partial class SqlServerRendererTests
{
    private static readonly SqlServerRenderer Renderer = new();
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly ResultQueryOptions Defaults = new() { Today = Today };

    // ---------- helpers ----------

    private static QueryModel Plan(string json, MetadataCatalog? catalog = null)
    {
        catalog ??= TestCatalog.Instance;
        var definition = DefinitionSerializer.Deserialize(json);
        new DefinitionValidator().Validate(definition, catalog).IsValid.ShouldBeTrue();
        return QueryPlanner.Plan(definition, catalog, ValidationLimits.Default);
    }

    private static QueryModel PlanOne(string field, string op, string? operandJson = null, string root = "employee") => Plan(
        $$"""
        { "schemaVersion": 1, "rootEntity": "{{root}}", "filter": { "type": "group", "logic": "and", "children": [
          { "type": "condition", "field": "{{field}}", "operator": "{{op}}"{{(operandJson is null ? "" : $", \"operand\": {operandJson}")}} } ] } }
        """);

    private static string Lit(string valueJson) => $$"""{ "kind": "literal", "value": {{valueJson}} }""";

    /// <summary>The WHERE clause of the summary query - the control's predicate on its own.</summary>
    private static string Predicate(QueryModel model) =>
        Renderer.RenderSummary(model, Today).Sql.Split("WHERE ", 2)[1].Split(") AS [violations]")[0];

    private static object ParamValue(RenderedQuery query, string name) => query.Parameters.Single(p => p.Name == name).Value;

    // ---------- golden SQL (readable spec of the output) ----------

    [Fact]
    public void Golden_summary_for_minors_with_high_salary()
    {
        var query = Renderer.RenderSummary(Plan(SampleDefinitions.MinorsWithHighSalary), Today);

        query.Sql.ShouldBe("""
            SELECT (SELECT COUNT_BIG(*) FROM [biz].[Employees]) AS [scanned],
                   (SELECT COUNT_BIG(*) FROM [biz].[Employees] AS r
                    WHERE (r.[BirthDate] > @p0 AND r.[Salary] > @p1)) AS [violations];
            """.ReplaceLineEndings("\n"));

        query.Parameters.ShouldBe(
        [
            new SqlParam("@p0", new DateOnly(2008, 11, 12), DbType.Date),          // under 18 ⇔ born after 12-Nov-2008
            new SqlParam("@p1", 15000m, DbType.Decimal, Precision: 28, Scale: 8),
        ]);
    }

    [Fact]
    public void Golden_page_for_active_department_without_active_employees()
    {
        var query = Renderer.RenderPage(Plan(SampleDefinitions.ActiveDepartmentWithoutActiveEmployees), Defaults);

        query.Sql.ShouldBe("""
            WITH v AS (
                SELECT r.[Id] AS [__id],
                       r.[Name] AS [__title],
                       r.[Name] AS [name],
                       r.[Code] AS [code],
                       r.[IsActive] AS [isActive],
                       CASE WHEN r.[IsActive] = @p0 THEN 1 ELSE 0 END AS [__c0],
                       CASE WHEN NOT EXISTS (SELECT 1 FROM [biz].[Employees] AS x1 WHERE x1.[DepartmentId] = r.[Id] AND (x1.[IsActive] = @p1)) THEN 1 ELSE 0 END AS [__c1]
                FROM [biz].[Departments] AS r
                WHERE (r.[IsActive] = @p0 AND NOT EXISTS (SELECT 1 FROM [biz].[Employees] AS x1 WHERE x1.[DepartmentId] = r.[Id] AND (x1.[IsActive] = @p1)))
            )
            SELECT *, COUNT(*) OVER () AS [__total]
            FROM v
            ORDER BY v.[__id] ASC
            OFFSET @p2 ROWS FETCH NEXT @p3 ROWS ONLY;
            """.ReplaceLineEndings("\n"));

        query.Parameters.Select(p => (p.Name, p.Value)).ShouldBe([("@p0", (object)true), ("@p1", true), ("@p2", 0L), ("@p3", 25L)]);
    }

    // ---------- security ----------

    [Theory]
    [MemberData(nameof(SampleDefinitions.All), MemberType = typeof(SampleDefinitions))]
    public void Produces_a_single_read_only_statement(string name, string json)
    {
        var model = Plan(json);

        foreach (var sql in new[] { Renderer.RenderPage(model, Defaults with { Search = "x" }).Sql, Renderer.RenderSummary(model, Today).Sql })
        {
            sql.ShouldStartWith(sql.StartsWith("WITH", StringComparison.Ordinal) ? "WITH v AS (" : "SELECT (", customMessage: name);
            sql.Count(c => c == ';').ShouldBe(1, name);
            sql.TrimEnd().ShouldEndWith(";");
            ForbiddenKeyword().IsMatch(sql).ShouldBeFalse(name);
        }
    }

    [GeneratedRegex(@"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|EXEC|EXECUTE|TRUNCATE|GRANT|INTO)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenKeyword();

    [Fact]
    public void A_malicious_value_never_reaches_the_SQL_text()
    {
        const string attack = "'; DROP TABLE biz.Employees; --";
        var model = PlanOne("fullName", "contains", Lit($"\"{attack}\""));

        var query = Renderer.RenderPage(model, Defaults with { Search = attack, Filters = [new ColumnFilter("employeeNumber", attack)] });

        query.Sql.ShouldNotContain("DROP");
        query.Sql.ShouldNotContain("'; ");
        query.Parameters.Count(p => p.Value is string s && s.Contains("DROP", StringComparison.Ordinal)).ShouldBe(3);
    }

    [Fact]
    public void A_hostile_identifier_from_the_catalog_stays_inside_its_brackets()
    {
        SqlIdentifier.Quote("Employees]; DROP TABLE x; --").ShouldBe("[Employees]]; DROP TABLE x; --]");

        Should.Throw<ArgumentException>(() => SqlIdentifier.Quote(""));
        Should.Throw<ArgumentException>(() => SqlIdentifier.Quote(new string('a', 129)));
        Should.Throw<ArgumentException>(() => SqlIdentifier.Quote("a\nb"));
        Should.Throw<ArgumentException>(() => SqlIdentifier.Alias("r; DROP"));
        SqlIdentifier.Alias("x1_j2").ShouldBe("x1_j2");
    }

    [Fact]
    public void Text_parameters_have_a_fixed_size_for_plan_reuse()
    {
        var query = Renderer.RenderSummary(PlanOne("employeeNumber", "eq", Lit("\"E10001\"")), Today);

        query.Parameters.Single().ShouldBe(new SqlParam("@p0", "E10001", DbType.String, Size: 4000));
    }

    // ---------- operators and NULL semantics ----------

    [Theory]
    [InlineData("salary", "eq", "5000", "r.[Salary] = @p0")]
    [InlineData("salary", "neq", "5000", "(r.[Salary] <> @p0 OR r.[Salary] IS NULL)")]
    [InlineData("salary", "gte", "5000", "r.[Salary] >= @p0")]
    [InlineData("salary", "between", "[5000, 9000]", "r.[Salary] BETWEEN @p0 AND @p1")]
    [InlineData("employeeNumber", "in", "[\"E1\", \"E2\", \"E3\"]", "r.[EmployeeNumber] IN (@p0, @p1, @p2)")]
    [InlineData("hireDate", "lt", "\"2020-01-01\"", "r.[HireDate] < @p0")]
    [InlineData("isActive", "eq", "false", "r.[IsActive] = @p0")]
    public void Renders_comparisons_with_parameters(string field, string op, string value, string expected) =>
        Predicate(PlanOne(field, op, Lit(value))).ShouldBe($"({expected})");

    [Theory]
    [InlineData("email", "isEmpty", "(r.[Email] IS NULL OR TRIM(r.[Email]) = N'')")]           // text: NULL or blank
    [InlineData("email", "isNotEmpty", "(r.[Email] IS NOT NULL AND TRIM(r.[Email]) <> N'')")]
    [InlineData("salary", "isEmpty", "r.[Salary] IS NULL")]
    [InlineData("terminationDate", "isNotEmpty", "r.[TerminationDate] IS NOT NULL")]
    public void Empty_means_null_or_blank_text(string field, string op, string expected) =>
        Predicate(PlanOne(field, op)).ShouldBe($"({expected})");

    [Fact]
    public void Field_to_field_comparisons_use_both_expressions()
    {
        Predicate(Plan(SampleDefinitions.TerminationBeforeHire)).ShouldBe("(r.[TerminationDate] < r.[HireDate])");
        Predicate(PlanOne("salary", "neq", """{ "kind": "field", "field": "manager.salary" }"""))
            .ShouldBe("((r.[Salary] <> j1.[Salary] OR (r.[Salary] IS NULL AND j1.[Salary] IS NOT NULL) OR (r.[Salary] IS NOT NULL AND j1.[Salary] IS NULL)))");
    }

    [Fact]
    public void Contains_escapes_like_wildcards_so_user_text_matches_literally()
    {
        var query = Renderer.RenderSummary(PlanOne("fullName", "contains", Lit("""" "50%_[x]\\" """".Trim())), Today);

        query.Sql.ShouldContain("CONCAT(r.[FirstName], N' ', r.[LastName]) LIKE @p0 ESCAPE N'\\'");
        ParamValue(query, "@p0").ShouldBe(@"%50\%\_\[x]\\%");
    }

    [Fact]
    public void Nested_groups_keep_their_parentheses()
    {
        Predicate(Plan(SampleDefinitions.ActiveMissingRequiredData)).ShouldBe(
            "(r.[IsActive] = @p0 AND ((r.[Email] IS NULL OR TRIM(r.[Email]) = N'') OR (r.[Phone] IS NULL OR TRIM(r.[Phone]) = N'') OR (j1.[Name] IS NULL OR TRIM(j1.[Name]) = N'')))");
    }

    [Fact]
    public void Joins_are_left_joins_on_the_relation_columns()
    {
        var sql = Renderer.RenderSummary(Plan(SampleDefinitions.EarnsMoreThanManager), Today).Sql;

        sql.ShouldContain("FROM [biz].[Employees] AS r\n        LEFT JOIN [biz].[Employees] AS j1 ON j1.[Id] = r.[ManagerId]");
        sql.ShouldContain("WHERE (r.[Salary] > j1.[Salary])");
    }

    // ---------- age: index-friendly rewrite ----------

    [Theory]
    [InlineData("gte", "18", "r.[BirthDate] <= @p0", 18)]
    [InlineData("gt", "18", "r.[BirthDate] <= @p0", 19)]
    [InlineData("lt", "18", "r.[BirthDate] > @p0", 18)]
    [InlineData("lte", "18", "r.[BirthDate] > @p0", 19)]
    public void Age_comparisons_become_birth_date_ranges(string op, string value, string expected, int cutoffAge)
    {
        var query = Renderer.RenderSummary(PlanOne("age", op, Lit(value)), Today);

        Predicate(PlanOne("age", op, Lit(value))).ShouldBe($"({expected})");
        query.Sql.ShouldNotContain("DATEDIFF");   // no per-row computation in the WHERE clause
        ParamValue(query, "@p0").ShouldBe(YearsSinceCutoff.LatestDateForYearsSince(Today, cutoffAge));
    }

    [Fact]
    public void Age_equality_ranges_and_lists_are_also_rewritten()
    {
        Predicate(PlanOne("age", "eq", Lit("30"))).ShouldBe("((r.[BirthDate] <= @p0 AND r.[BirthDate] > @p1))");
        Predicate(PlanOne("age", "neq", Lit("30"))).ShouldBe("((r.[BirthDate] > @p0 OR r.[BirthDate] <= @p1 OR r.[BirthDate] IS NULL))");
        Predicate(PlanOne("age", "between", Lit("[20, 30]"))).ShouldBe("((r.[BirthDate] <= @p0 AND r.[BirthDate] > @p1))");
        Predicate(PlanOne("age", "in", Lit("[20, 30]")))
            .ShouldBe("(((r.[BirthDate] <= @p0 AND r.[BirthDate] > @p1) OR (r.[BirthDate] <= @p2 AND r.[BirthDate] > @p3)))");
        Predicate(PlanOne("age", "isEmpty")).ShouldBe("(r.[BirthDate] IS NULL)");
    }

    [Fact]
    public void Age_is_displayed_with_the_exact_expression_and_today_as_a_parameter()
    {
        var query = Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary), Defaults);

        query.Sql.ShouldContain("(DATEDIFF(YEAR, r.[BirthDate], @today) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, r.[BirthDate], @today), r.[BirthDate]) > @today THEN 1 ELSE 0 END) AS [age]");
        ParamValue(query, "@today").ShouldBe(Today);
    }

    [Fact]
    public void Age_compared_to_another_field_falls_back_to_the_expression()
    {
        Predicate(PlanOne("age", "lt", """{ "kind": "field", "field": "salary" }""")).ShouldContain("DATEDIFF(YEAR, r.[BirthDate], @today)");
    }

    // ---------- results options ----------

    [Fact]
    public void Paging_uses_typed_offset_and_take()
    {
        var query = Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary), Defaults with { Page = 3, PageSize = 20 });

        query.Sql.ShouldEndWith("OFFSET @p2 ROWS FETCH NEXT @p3 ROWS ONLY;");
        ParamValue(query, "@p2").ShouldBe(40L);
        ParamValue(query, "@p3").ShouldBe(20L);
    }

    [Fact]
    public void Sorting_is_whitelisted_and_stable()
    {
        var model = Plan(SampleDefinitions.MinorsWithHighSalary);

        Renderer.RenderPage(model, Defaults with { SortKey = "salary", SortDescending = true }).Sql
            .ShouldContain("ORDER BY v.[salary] DESC, v.[__id] ASC");
        Renderer.RenderPage(model, Defaults with { SortKey = "__title" }).Sql.ShouldContain("ORDER BY v.[__title] ASC");
        Should.Throw<QueryOptionsException>(() => Renderer.RenderPage(model, Defaults with { SortKey = "Salary; DROP TABLE x" }));
        Should.Throw<QueryOptionsException>(() => Renderer.RenderPage(model, Defaults with { SortKey = "budget" }));
    }

    [Fact]
    public void Search_covers_searchable_text_columns_once()
    {
        var query = Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary), Defaults with { Search = "  כהן  " });

        query.Sql.ShouldContain("WHERE (v.[employeeNumber] LIKE @p2 ESCAPE N'\\' OR v.[fullName] LIKE @p2 ESCAPE N'\\')");
        ParamValue(query, "@p2").ShouldBe("%כהן%");
    }

    [Fact]
    public void Column_filters_are_typed_by_the_column()
    {
        var model = Plan(SampleDefinitions.MinorsWithHighSalary);
        var query = Renderer.RenderPage(model, Defaults with
        {
            Filters = [new("fullName", "כהן"), new("isActive", "true"), new("salary", "22000.5"), new("age", "16"), new("employeeNumber", " ")],
        });

        query.Sql.ShouldContain("v.[fullName] LIKE @p2 ESCAPE N'\\'\n  AND v.[isActive] = @p3\n  AND v.[salary] = @p4\n  AND v.[age] = @p5");
        query.Sql.ShouldNotContain("v.[employeeNumber] ="); // blank filter ignored
        (ParamValue(query, "@p3"), ParamValue(query, "@p4"), ParamValue(query, "@p5")).ShouldBe((true, 22000.5m, 16L));
    }

    [Theory]
    [InlineData("salary", "lots")]
    [InlineData("age", "16.5")]
    [InlineData("isActive", "maybe")]
    [InlineData("nosuch", "x")]
    public void Rejects_invalid_filters_with_a_Hebrew_message(string key, string value) =>
        Should.Throw<QueryOptionsException>(() => Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary),
            Defaults with { Filters = [new(key, value)] })).Message.Any(c => c is >= 'א' and <= 'ת').ShouldBeTrue();

    [Theory]
    [InlineData(0, 25)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public void Rejects_invalid_paging(int page, int pageSize) =>
        Should.Throw<QueryOptionsException>(() => Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary),
            Defaults with { Page = page, PageSize = pageSize }));

    [Fact]
    public void Rejects_an_overlong_search() =>
        Should.Throw<QueryOptionsException>(() => Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary),
            Defaults with { Search = new string('א', ValidationLimits.Default.MaxSearchLength + 1) }));

    [Fact]
    public void Rejects_an_overlong_filter_value() =>
        Should.Throw<QueryOptionsException>(() => Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary),
            Defaults with { Filters = [new("fullName", new string('א', ValidationLimits.Default.MaxSearchLength + 1))] }))
            .Message.ShouldBe($"ערך הסינון של 'שם מלא' ארוך מדי (עד {ValidationLimits.Default.MaxSearchLength} תווים).");

    [Fact]
    public void Rejects_more_than_one_filter_per_column()
    {
        // Regression: 3,000 filters on one column passed SQL Server's 2,100-parameter limit and failed with a 500.
        var many = Enumerable.Repeat(new ColumnFilter("fullName", "כהן"), 3000).ToList();

        Should.Throw<QueryOptionsException>(() => Renderer.RenderPage(Plan(SampleDefinitions.MinorsWithHighSalary), Defaults with { Filters = many }))
            .Message.ShouldBe("נשלח יותר מסינון אחד לעמודה 'שם מלא'.");
    }

    [Theory]
    [MemberData(nameof(SampleDefinitions.All), MemberType = typeof(SampleDefinitions))]
    public void Rendering_is_deterministic(string name, string json)
    {
        var model = Plan(json);

        Renderer.RenderPage(model, Defaults).ShouldBeEquivalentTo(Renderer.RenderPage(model, Defaults), name);
    }
}
