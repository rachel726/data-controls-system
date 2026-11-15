using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DataControls.Application.Ai;
using DataControls.Core.Definitions;
using DataControls.Core.Tests.Fixtures;
using DataControls.Core.Validation;
using DataControls.Infrastructure.Ai;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace DataControls.IntegrationTests.Ai;

/// <summary>The HTTP client for the Python AI service: every way the service can answer becomes one of two errors.</summary>
public class PythonAiServiceClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static (PythonAiServiceClient Client, StubHandler Handler) Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://ai.test") };
        return (new PythonAiServiceClient(http, NullLogger<PythonAiServiceClient>.Instance), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };

    private static SuggestRequest Request => new("עובדים מתחת לגיל 18", SchemaContextBuilder.Build(TestCatalog.Instance, ValidationLimits.Default));

    [Fact]
    public async Task Sends_the_request_in_the_contract_and_reads_the_suggestion()
    {
        var (client, handler) = Client(_ => Json(HttpStatusCode.OK, AiMockFixtures.Response(AiMockFixtures.MinorsHighSalary)));

        var suggestion = await client.SuggestAsync(Request, CancellationToken.None);

        handler.LastUri!.ToString().ShouldBe("http://ai.test/v1/suggest-control");
        var sent = JsonDocument.Parse(handler.LastBody!).RootElement;
        sent.GetProperty("text").GetString().ShouldBe("עובדים מתחת לגיל 18");
        sent.GetProperty("context").GetProperty("entities").GetArrayLength().ShouldBe(TestCatalog.Instance.Entities.Count);
        suggestion.RootEntity.ShouldBe("employee");
        suggestion.Conditions!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_answer_the_ai_service_rejected_is_an_invalid_answer()
    {
        var (client, _) = Client(_ => Json(HttpStatusCode.BadGateway,
            """{ "status": 502, "code": "invalidResponse", "detail": "תשובת ה-AI אינה במבנה הצפוי" }""", "application/problem+json"));

        await Should.ThrowAsync<AiResponseInvalidException>(() => client.SuggestAsync(Request, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, """{ "code": "providerUnavailable" }""")]
    [InlineData(HttpStatusCode.InternalServerError, "oops")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{ "detail": [] }""")]
    public async Task Any_other_failure_means_the_ai_is_unavailable(HttpStatusCode status, string body)
    {
        var (client, _) = Client(_ => Json(status, body));

        await Should.ThrowAsync<AiUnavailableException>(() => client.SuggestAsync(Request, CancellationToken.None));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{ "rootEntity": "employee", "conditions": [ { "field": "age", "operator": "lessThan" } ] }""")]
    [InlineData("""{ "rootEntity": "employee", "sql": "DELETE FROM x" }""")]
    public async Task A_success_answer_outside_the_contract_is_an_invalid_answer(string body)
    {
        var (client, _) = Client(_ => Json(HttpStatusCode.OK, body));

        await Should.ThrowAsync<AiResponseInvalidException>(() => client.SuggestAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task Reads_the_examples_the_service_offers()
    {
        var (client, handler) = Client(_ => Json(HttpStatusCode.OK, """[{ "label": "קטינים", "text": "עובדים מתחת לגיל 18" }]"""));

        var examples = await client.GetExamplesAsync(CancellationToken.None);

        handler.LastUri!.ToString().ShouldBe("http://ai.test/v1/examples");
        examples.ShouldBe([new AiExample("קטינים", "עובדים מתחת לגיל 18")]);
    }

    [Fact]
    public async Task Examples_from_a_failing_service_are_unavailable() =>
        await Should.ThrowAsync<AiUnavailableException>(() =>
            Client(_ => Json(HttpStatusCode.InternalServerError, "{}")).Client.GetExamplesAsync(CancellationToken.None));

    [Fact]
    public async Task An_unreachable_service_is_unavailable()
    {
        var (client, _) = Client(_ => throw new HttpRequestException("connection refused"));

        await Should.ThrowAsync<AiUnavailableException>(() => client.SuggestAsync(Request, CancellationToken.None));
    }

    [Fact]
    public async Task The_registered_client_with_its_resilience_pipeline_reports_a_dead_service_quickly()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.UseSetting("AiService:BaseUrl", "http://127.0.0.1:9").UseSetting("AiService:TimeoutSeconds", "1"));
        using var scope = factory.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IControlSuggestionClient>();
        var watch = Stopwatch.StartNew();

        await Should.ThrowAsync<AiUnavailableException>(() => client.SuggestAsync(Request, CancellationToken.None));

        // the configured 1 s, retry included, far below the 20 s default; the slack absorbs a busy machine
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1 + 4));
    }
}

/// <summary>
/// POST /api/ai/suggest-control on the real API and the real catalog (database), with the AI service replaced by
/// its own mock answers: each one must translate to a valid control - except the error demo, which must not.
/// </summary>
public sealed class AiApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class MockAnswers : IControlSuggestionClient
    {
        public Task<ControlSuggestion> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken)
        {
            var fixture = AiMockFixtures.All().FirstOrDefault(f => AiMockFixtures.Request(f) == request.Text);
            return Task.FromResult(fixture is null
                ? new ControlSuggestion(Explanation: "לא הצלחתי להבין את הבקשה.", UnmappedTerms: [request.Text])
                : JsonSerializer.Deserialize<ControlSuggestion>(AiMockFixtures.Response(fixture), DefinitionSerializer.Options)!);
        }

        /// <summary>As the Python mock serves them: its fixtures with an "example" label, sending their first request.</summary>
        public Task<IReadOnlyList<AiExample>> GetExamplesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiExample>>(
                [.. AiMockFixtures.All().Where(f => AiMockFixtures.Example(f) is not null).Select(f => new AiExample(AiMockFixtures.Example(f)!, AiMockFixtures.Request(f)))]);
    }

    private sealed class DeadAi : IControlSuggestionClient
    {
        public Task<ControlSuggestion> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken) =>
            throw new AiUnavailableException(AiUnavailableException.DefaultMessage);

        public Task<IReadOnlyList<AiExample>> GetExamplesAsync(CancellationToken cancellationToken) =>
            throw new AiUnavailableException(AiUnavailableException.DefaultMessage);
    }

    [Fact]
    public async Task The_examples_come_from_the_ai_and_each_translates_to_a_control()
    {
        var api = Api(new MockAnswers());
        var examples = JsonDocument.Parse(await api.GetStringAsync("/api/ai/examples")).RootElement;

        examples.GetArrayLength().ShouldBe(5);
        examples[0].GetProperty("label").GetString().ShouldBe("עובדים פעילים מתחת לגיל 18 עם שכר גבוה");
        foreach (var example in examples.EnumerateArray())
        {
            var body = await Suggest(api, example.GetProperty("text").GetString(), HttpStatusCode.OK);
            body.GetProperty("understood").GetBoolean().ShouldBeTrue(example.GetProperty("label").GetString());
        }
    }

    [Fact]
    public async Task Examples_of_an_unavailable_ai_are_a_503() =>
        (await Api(new DeadAi()).GetAsync("/api/ai/examples")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

    private HttpClient Api(IControlSuggestionClient ai) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(ai))).CreateClient();

    private static async Task<JsonElement> Suggest(HttpClient api, string? text, HttpStatusCode expected)
    {
        var response = await api.PostAsJsonAsync("/api/ai/suggest-control", new { text });
        response.StatusCode.ShouldBe(expected);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    public static TheoryData<string> UsableAnswers => new(AiMockFixtures.All().Where(f => f != AiMockFixtures.ErrorDemo));

    [Theory]
    [MemberData(nameof(UsableAnswers))]
    public async Task Every_mock_answer_becomes_a_valid_control_on_the_real_catalog(string fixture)
    {
        var body = await Suggest(Api(new MockAnswers()), AiMockFixtures.Request(fixture), HttpStatusCode.OK);

        body.GetProperty("understood").GetBoolean().ShouldBeTrue();
        body.GetProperty("isValid").GetBoolean().ShouldBeTrue(body.GetProperty("validationErrors").ToString());
        body.GetProperty("definitionDescription").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("definition").GetProperty("filter").GetProperty("type").GetString().ShouldBe("group");
    }

    [Fact]
    public async Task The_stage_b_example_end_to_end()
    {
        var body = await Suggest(Api(new MockAnswers()), AiMockFixtures.Request(AiMockFixtures.ActiveMinorsHighSalary), HttpStatusCode.OK);

        body.GetProperty("definitionDescription").GetString().ShouldBe("עובד פעיל וגם גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000 ₪");
        body.GetProperty("assumptions").GetArrayLength().ShouldBeGreaterThan(0);
        body.GetProperty("explanation").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_error_demo_is_shown_with_the_reason_instead_of_a_meaning()
    {
        var body = await Suggest(Api(new MockAnswers()), AiMockFixtures.Request(AiMockFixtures.ErrorDemo), HttpStatusCode.OK);

        body.GetProperty("isValid").GetBoolean().ShouldBeFalse();
        body.GetProperty("definitionDescription").ValueKind.ShouldBe(JsonValueKind.Null);
        var error = body.GetProperty("validationErrors")[0];
        error.GetProperty("path").GetString().ShouldBe("filter.children[0].field");
        error.GetProperty("message").GetString()!.ShouldContain("bonus");
    }

    [Fact]
    public async Task A_request_the_ai_did_not_understand_is_a_normal_answer()
    {
        var body = await Suggest(Api(new MockAnswers()), "מה מזג האוויר?", HttpStatusCode.OK);

        body.GetProperty("understood").GetBoolean().ShouldBeFalse();
        body.GetProperty("definition").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_unavailable_ai_is_a_503_problem_in_Hebrew()
    {
        var problem = await Suggest(Api(new DeadAi()), "עובדים", HttpStatusCode.ServiceUnavailable);

        problem.GetProperty("title").GetString().ShouldBe("שירות ה-AI אינו זמין.");
        problem.GetProperty("detail").GetString().ShouldBe(AiUnavailableException.DefaultMessage);
    }

    [Fact]
    public async Task An_empty_request_is_a_400_on_the_text_field()
    {
        var problem = await Suggest(Api(new MockAnswers()), "  ", HttpStatusCode.BadRequest);

        problem.GetProperty("errors").GetProperty("text").GetArrayLength().ShouldBe(1);
    }
}
