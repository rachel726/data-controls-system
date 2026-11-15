using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DataControls.Core.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace DataControls.IntegrationTests.Api;

/// <summary>
/// The 7 sample controls of database/scripts/06_seed_sample_controls.sql: the script holds exactly the tested
/// definitions, and each definition, run end to end through the API, finds exactly the violations planted by
/// 05_seed_business_data.sql, each with a Hebrew reason. Neither test reads the seeded rows, which users may edit
/// through the UI - each run uses its own temporary control.
/// </summary>
public sealed partial class SampleControlsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string, string, int, int> Samples => new()
    {
        { "עובדים מתחת לגיל 18 עם שכר גבוה", SampleDefinitions.MinorsWithHighSalary, 3, 222 },
        { "תאריך סיום העסקה לפני תאריך תחילת העסקה", SampleDefinitions.TerminationBeforeHire, 4, 222 },
        { "עובדים פעילים במחלקה לא פעילה", SampleDefinitions.ActiveInInactiveDepartment, 5, 222 },
        { "עובדים פעילים עם נתוני חובה חסרים", SampleDefinitions.ActiveMissingRequiredData, 6, 222 },
        { "עובדים שמרוויחים יותר מהמנהל הישיר", SampleDefinitions.EarnsMoreThanManager, 3, 222 },
        { "פרויקטים שתאריך הסיום שלהם לפני תאריך ההתחלה", SampleDefinitions.ProjectEndBeforeStart, 2, 20 },
        { "מחלקות פעילות ללא עובדים פעילים", SampleDefinitions.ActiveDepartmentWithoutActiveEmployees, 1, 10 },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void The_setup_script_holds_exactly_the_tested_definition(string name, string expectedJson, int violations, int scanned)
    {
        _ = (violations, scanned);

        var seeded = SeedScriptDefinitions().GetValueOrDefault(name);

        seeded.ShouldNotBeNull($"'{name}' is missing from 06_seed_sample_controls.sql");
        JsonNode.DeepEquals(JsonNode.Parse(seeded), JsonNode.Parse(expectedJson)).ShouldBeTrue(name);
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Sample_control_runs_through_the_API_and_finds_exactly_the_planted_violations(
        string name, string json, int expectedViolations, int expectedScanned)
    {
        var id = await CreateAsync($"בדיקת דוגמה זמנית {Guid.NewGuid():N} {name}", json);
        try
        {
            var response = await _client.PostAsync($"/api/controls/{id}/run", null);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var run = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            run.GetProperty("violationCount").GetInt64().ShouldBe(expectedViolations, name);
            run.GetProperty("totalScanned").GetInt64().ShouldBe(expectedScanned, name);

            var rows = run.GetProperty("rows");
            rows.GetArrayLength().ShouldBe(expectedViolations);
            foreach (var row in rows.EnumerateArray())
            {
                row.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
                var reasons = row.GetProperty("reasons");
                reasons.GetArrayLength().ShouldBeGreaterThan(0, name);
                reasons.EnumerateArray().ShouldAllBe(r => r.GetString()!.Any(c => c >= 'א' && c <= 'ת'));
            }

            var lastRun = JsonDocument.Parse(await _client.GetStringAsync($"/api/controls/{id}")).RootElement.GetProperty("lastRun");
            lastRun.GetProperty("status").GetString().ShouldBe("succeeded");
            lastRun.GetProperty("violationCount").GetInt32().ShouldBe(expectedViolations);
        }
        finally
        {
            (await _client.DeleteAsync($"/api/controls/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    private async Task<int> CreateAsync(string name, string definitionJson)
    {
        var body = new JsonObject { ["name"] = name, ["description"] = null, ["definition"] = JsonNode.Parse(definitionJson) };
        var response = await _client.PostAsync("/api/controls", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetInt32();
    }

    /// <summary>Name → definition JSON, as written in the setup script's rows: (order, N'name', N'description', 'root', N'json').</summary>
    private static Dictionary<string, string> SeedScriptDefinitions() =>
        SeedRow().Matches(File.ReadAllText(SeedScriptPath()))
            .ToDictionary(m => Unquote(m.Groups["name"].Value), m => Unquote(m.Groups["json"].Value), StringComparer.Ordinal);

    [GeneratedRegex(@"\(\d+,\s*N'(?<name>(?:[^']|'')*)',\s*N'(?:[^']|'')*',\s*'\w+',\s*N'(?<json>(?:[^']|'')*)'\)")]
    private static partial Regex SeedRow();

    private static string Unquote(string sqlText) => sqlText.Replace("''", "'", StringComparison.Ordinal);

    private static string SeedScriptPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "database", "scripts", "06_seed_sample_controls.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException("database/scripts/06_seed_sample_controls.sql not found above the test folder.");
    }
}
