using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataControls.Core.Catalog;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace DataControls.IntegrationTests.Api;

/// <summary>
/// The real API (in-process, real database): routes, JSON contract, status codes, Hebrew ProblemDetails.
/// Every control created here uses a unique name prefix and is removed afterwards.
/// </summary>
public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _prefix = $"api-{Guid.NewGuid():N}"[..16];

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("DELETE FROM meta.Controls WHERE Name LIKE @p + '%';", connection);
        command.Parameters.AddWithValue("@p", _prefix);
        await command.ExecuteNonQueryAsync();
    }

    // ---------- helpers ----------

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private string SaveBody(string name, string definitionJson, string? description = null) =>
        $$"""{ "name": "{{_prefix}} {{name}}", "description": {{(description is null ? "null" : $"\"{description}\"")}}, "definition": {{definitionJson}} }""";

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<JsonElement> Problem(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await Body(response);
        body.GetProperty("status").GetInt32().ShouldBe((int)status);
        body.GetProperty("title").GetString()!.Any(c => c is >= 'א' and <= 'ת').ShouldBeTrue("title must be Hebrew");
        body.TryGetProperty("traceId", out _).ShouldBeTrue("traceId lets support find the error in the logs");
        return body;
    }

    // ---------- catalog ----------

    [Fact]
    public async Task Catalog_exposes_business_terms_only()
    {
        var response = await _client.GetAsync("/api/catalog/entities");
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entities = JsonDocument.Parse(text).RootElement;
        entities.GetArrayLength().ShouldBe(3);
        var age = entities[0].GetProperty("fields").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "age");
        age.GetProperty("displayName").GetString().ShouldBe("גיל");
        age.GetProperty("dataType").GetString().ShouldBe("integer");                   // camelCase string enums everywhere

        // No physical schema leaks to the client (case-sensitive: "employees" is a business relation key, "Employees" the table).
        foreach (var technical in new[] { "\"biz\"", "Employees", "BirthDate", "DepartmentId", "sourceColumn", "tableName", "schemaName" })
        {
            text.ShouldNotContain(technical, Case.Sensitive);
        }
    }

    [Fact]
    public async Task Each_entity_lists_the_fields_a_control_may_use_worded_by_the_server()
    {
        var employee = (await Body(await _client.GetAsync("/api/catalog/entities"))).EnumerateArray().Single(e => e.GetProperty("key").GetString() == "employee");

        var department = employee.GetProperty("fieldPaths").EnumerateArray().Single(f => f.GetProperty("path").GetString() == "department.name");
        (department.GetProperty("label").GetString(), department.GetProperty("owner").GetString(),
         department.GetProperty("entityKey").GetString(), department.GetProperty("fieldKey").GetString())
            .ShouldBe(("שם המחלקה", "מחלקה", "department", "name"));
    }

    [Fact]
    public async Task Operators_are_served_with_Hebrew_labels_and_rules()
    {
        var operators = await Body(await _client.GetAsync("/api/catalog/operators"));

        var lt = operators.EnumerateArray().Single(o => o.GetProperty("key").GetString() == "lt");
        (lt.GetProperty("label").GetString(), lt.GetProperty("dateLabel").GetString()).ShouldBe(("קטן מ", "לפני"));
        lt.GetProperty("shape").GetString().ShouldBe("value");
        lt.GetProperty("allowsFieldOperand").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Limits_are_served_by_the_server_that_enforces_them()
    {
        var limits = await Body(await _client.GetAsync("/api/catalog/limits"));

        (limits.GetProperty("maxConditions").GetInt32(), limits.GetProperty("maxDepth").GetInt32(), limits.GetProperty("maxRelationHops").GetInt32())
            .ShouldBe((ValidationLimits.Default.MaxConditions, ValidationLimits.Default.MaxDepth, ValidationLimits.Default.MaxRelationHops));
        (limits.GetProperty("maxNameLength").GetInt32(), limits.GetProperty("maxDescriptionLength").GetInt32(), limits.GetProperty("maxAiTextLength").GetInt32())
            .ShouldBe((ValidationLimits.Default.MaxNameLength, ValidationLimits.Default.MaxDescriptionLength, ValidationLimits.Default.MaxAiTextLength));
        (limits.GetProperty("maxTextValueLength").GetInt32(), limits.GetProperty("maxSearchLength").GetInt32())
            .ShouldBe((ValidationLimits.Default.MaxTextValueLength, ValidationLimits.Default.MaxSearchLength));
    }

    // ---------- full lifecycle ----------

    [Fact]
    public async Task Create_run_browse_update_and_delete_a_control()
    {
        // create
        var created = await _client.PostAsync("/api/controls", Json(SaveBody("קטינים", SampleDefinitions.MinorsWithHighSalary, "בדיקת גיל")));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.Location.ShouldNotBeNull();
        var control = await Body(created);
        var id = control.GetProperty("id").GetInt32();
        control.GetProperty("definitionDescription").GetString().ShouldBe("גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000 ₪");
        control.GetProperty("definition").GetProperty("filter").GetProperty("children")[0].GetProperty("operator").GetString().ShouldBe("lt");

        // list + search
        var list = await Body(await _client.GetAsync($"/api/controls?search={Uri.EscapeDataString(_prefix)}"));
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("rootEntityName").GetString().ShouldBe("עובדים");
        list[0].GetProperty("lastRun").ValueKind.ShouldBe(JsonValueKind.Null);

        // run
        var run = await Body(await _client.PostAsync($"/api/controls/{id}/run", null));
        run.GetProperty("violationCount").GetInt64().ShouldBe(3);
        run.GetProperty("totalScanned").GetInt64().ShouldBe(222);
        var row = run.GetProperty("rows")[0];
        row.GetProperty("reasons")[0].GetString()!.ShouldStartWith("גיל (");
        row.GetProperty("values").GetProperty("salary").GetString()!.ShouldEndWith("₪");

        var afterRun = (await Body(await _client.GetAsync($"/api/controls?search={Uri.EscapeDataString(_prefix)}")))[0].GetProperty("lastRun");
        (afterRun.GetProperty("status").GetString(), afterRun.GetProperty("violationCount").GetInt32()).ShouldBe(("succeeded", 3));

        // browse results: sort + page size
        var results = await Body(await _client.PostAsync($"/api/controls/{id}/results",
            Json("""{ "page": 1, "pageSize": 2, "sortKey": "salary", "sortDescending": true }""")));
        results.GetProperty("rows").GetArrayLength().ShouldBe(2);
        results.GetProperty("totalFiltered").GetInt64().ShouldBe(3);
        results.GetProperty("violationCount").GetInt64().ShouldBe(3);                     // counted by default

        // browsing without the summary: the same page, no counts
        var browsed = await Body(await _client.PostAsync($"/api/controls/{id}/results",
            Json("""{ "page": 1, "pageSize": 2, "sortKey": "salary", "sortDescending": true, "includeSummary": false }""")));
        browsed.GetProperty("rows").GetRawText().ShouldBe(results.GetProperty("rows").GetRawText());
        (browsed.GetProperty("violationCount").ValueKind, browsed.GetProperty("totalScanned").ValueKind).ShouldBe((JsonValueKind.Null, JsonValueKind.Null));

        // update clears the last run
        var updated = await Body(await _client.PutAsync($"/api/controls/{id}", Json(SaveBody("פרויקטים", SampleDefinitions.ProjectEndBeforeStart))));
        updated.GetProperty("definitionDescription").GetString().ShouldBe("תאריך סיום לפני תאריך התחלה");
        updated.GetProperty("lastRun").ValueKind.ShouldBe(JsonValueKind.Null);

        // delete → gone
        (await _client.DeleteAsync($"/api/controls/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var missing = await Problem(await _client.GetAsync($"/api/controls/{id}"), HttpStatusCode.NotFound);
        missing.GetProperty("detail").GetString()!.ShouldContain("לא נמצאה");
    }

    [Fact]
    public async Task Validate_returns_the_sentence_or_the_errors()
    {
        var ok = await Body(await _client.PostAsync("/api/controls/validate", Json(SampleDefinitions.ActiveDepartmentWithoutActiveEmployees)));
        ok.GetProperty("isValid").GetBoolean().ShouldBeTrue();
        ok.GetProperty("description").GetString().ShouldBe("מחלקה פעילה וגם אין אף עובד פעיל");

        var bad = await Body(await _client.PostAsync("/api/controls/validate", Json(
            """{ "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "and", "children": [ { "type": "condition", "field": "shoeSize", "operator": "isEmpty" } ] } }""")));
        bad.GetProperty("isValid").GetBoolean().ShouldBeFalse();
        bad.GetProperty("errors")[0].GetProperty("path").GetString().ShouldBe("filter.children[0].field");
        bad.GetProperty("errors")[0].GetProperty("isIncomplete").GetBoolean().ShouldBeFalse();   // a field that does not exist is a mistake

        var unfinished = await Body(await _client.PostAsync("/api/controls/validate", Json(
            """{ "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "and", "children": [ { "type": "condition", "field": "age", "operator": "lt" } ] } }""")));
        unfinished.GetProperty("errors")[0].GetProperty("isIncomplete").GetBoolean().ShouldBeTrue();   // no value yet
    }

    // ---------- errors ----------

    [Fact]
    public async Task An_invalid_definition_is_a_400_with_errors_pointing_at_nodes()
    {
        var body = await Problem(await _client.PostAsync("/api/controls", Json(SaveBody("שגוי",
            """{ "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": "and", "children": [ { "type": "condition", "field": "age", "operator": "contains", "operand": { "kind": "literal", "value": "x" } } ] } }"""))),
            HttpStatusCode.BadRequest);

        var error = body.GetProperty("definitionErrors")[0];
        error.GetProperty("path").GetString().ShouldBe("filter.children[0].operator");
        error.GetProperty("message").GetString()!.ShouldContain("גיל");
    }

    [Fact]
    public async Task Missing_fields_are_a_400_with_Hebrew_messages_per_field()
    {
        var body = await Problem(await _client.PostAsync("/api/controls", Json("""{ "name": "  " }""")), HttpStatusCode.BadRequest);

        var errors = body.GetProperty("errors");
        errors.GetProperty("name")[0].GetString().ShouldBe("יש להזין שם לבקרה.");
        errors.GetProperty("definition")[0].GetString().ShouldBe("יש להגדיר את תנאי הבקרה.");
    }

    [Fact]
    public async Task A_duplicate_name_is_a_409()
    {
        (await _client.PostAsync("/api/controls", Json(SaveBody("כפול", SampleDefinitions.MinorsWithHighSalary)))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = await Problem(await _client.PostAsync("/api/controls", Json(SaveBody("כפול", SampleDefinitions.MinorsWithHighSalary))), HttpStatusCode.Conflict);
        body.GetProperty("detail").GetString()!.ShouldContain("כבר קיימת בקרה");
    }

    [Theory]
    [InlineData("{ not json")]                                                                                   // malformed
    [InlineData("""{ "name": "x", "sql": "DROP TABLE biz.Employees" }""")]                                      // unknown property (strict)
    [InlineData("""{ "name": "x", "definition": { "schemaVersion": 1, "rootEntity": "employee", "filter": { "type": "group", "logic": 0, "children": [] } } }""")]  // numeric enum
    public async Task A_malformed_body_is_a_400_problem(string json)
    {
        var response = await _client.PostAsync("/api/controls", Json(json));
        var body = await Problem(response, HttpStatusCode.BadRequest);

        // Hebrew per property, never the framework's text (it names internal .NET types).
        body.GetProperty("title").GetString().ShouldBe("הבקשה אינה תקינה.");
        var text = await response.Content.ReadAsStringAsync();
        text.ShouldNotContain("DataControls.");
        text.ShouldNotContain(".NET");
        text.ShouldNotContain("could not be");
    }

    [Fact]
    public async Task An_unknown_property_is_named_in_Hebrew()
    {
        var body = await Problem(await _client.PostAsync("/api/controls", Json("""{ "name": "x", "sql": "DROP TABLE biz.Employees" }""")),
            HttpStatusCode.BadRequest);

        body.GetProperty("errors").GetProperty("sql")[0].GetString().ShouldBe("הערך של 'sql' אינו תקין או אינו מוכר.");
        body.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["sql"]);   // no generic "request" entry
    }

    [Fact]
    public async Task A_page_past_the_end_serves_the_last_page_with_the_real_total()
    {
        var id = (await Body(await _client.PostAsync("/api/controls", Json(SaveBody("עמוד", SampleDefinitions.MinorsWithHighSalary))))).GetProperty("id").GetInt32();
        await _client.PostAsync($"/api/controls/{id}/run", null);

        var page = await Body(await _client.PostAsync($"/api/controls/{id}/results", Json("""{ "page": 99, "pageSize": 2 }""")));

        page.GetProperty("totalFiltered").GetInt64().ShouldBe(3);
        page.GetProperty("page").GetInt32().ShouldBe(2);                    // the last page that exists (3 rows / 2 per page)
        page.GetProperty("rows").GetArrayLength().ShouldBe(1);

        var none = await Body(await _client.PostAsync($"/api/controls/{id}/results", Json("""{ "page": 5, "search": "אין-כזה" }""")));
        none.GetProperty("totalFiltered").GetInt64().ShouldBe(0);
        none.GetProperty("page").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_result_options_are_a_400()
    {
        var id = (await Body(await _client.PostAsync("/api/controls", Json(SaveBody("מיון", SampleDefinitions.MinorsWithHighSalary))))).GetProperty("id").GetInt32();

        var body = await Problem(await _client.PostAsync($"/api/controls/{id}/results", Json("""{ "sortKey": "budget" }""")), HttpStatusCode.BadRequest);
        body.GetProperty("detail").GetString().ShouldBe("לא ניתן למיין לפי עמודה זו.");
        await Problem(await _client.PostAsync($"/api/controls/{id}/results", Json("""{ "page": 1, "pageSize": 5000 }""")), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_routes_and_ids_are_404()
    {
        await Problem(await _client.PostAsync("/api/controls/999999/run", null), HttpStatusCode.NotFound);
        var unknown = await Problem(await _client.GetAsync("/api/nothing"), HttpStatusCode.NotFound);
        unknown.GetProperty("title").GetString().ShouldBe("הכתובת המבוקשת לא נמצאה.");
        (await Problem(await _client.GetAsync("/api/controls/abc"), HttpStatusCode.NotFound)).GetProperty("title").GetString()
            .ShouldBe("הכתובת המבוקשת לא נמצאה.");
    }

    [Fact]
    public async Task An_unexpected_error_is_a_generic_500_without_internals()
    {
        using var failing = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<ICatalogProvider>(new ThrowingCatalogProvider())));

        var response = await failing.CreateClient().GetAsync("/api/catalog/entities");
        var body = await Problem(response, HttpStatusCode.InternalServerError);

        body.GetProperty("title").GetString().ShouldBe("אירעה שגיאה בלתי צפויה.");
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("connection string secret");
    }

    private sealed class ThrowingCatalogProvider : ICatalogProvider
    {
        public Task<MetadataCatalog> GetCatalogAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("connection string secret: Server=prod;Password=123");
    }
}
