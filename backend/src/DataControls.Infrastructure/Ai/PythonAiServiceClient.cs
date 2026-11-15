using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DataControls.Application.Ai;
using DataControls.Core.Definitions;
using Microsoft.Extensions.Logging;
using Polly;

namespace DataControls.Infrastructure.Ai;

public sealed class AiServiceOptions
{
    public const string Section = "AiService";

    public Uri BaseUrl { get; set; } = new("http://localhost:8001");

    /// <summary>How long to wait for a suggestion (the model is slow); a retry stays inside this limit.</summary>
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>
/// Calls the Python AI service (POST /v1/suggest-control, GET /v1/examples). Its failures become two application errors only:
/// unavailable (unreachable, timed out, failed) or invalid answer (outside the contract) - callers never see HTTP.
/// </summary>
public sealed partial class PythonAiServiceClient(HttpClient http, ILogger<PythonAiServiceClient> logger) : IControlSuggestionClient
{
    private const string SuggestPath = "v1/suggest-control";
    private const string ExamplesPath = "v1/examples";

    public async Task<IReadOnlyList<AiExample>> GetExamplesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await http.GetFromJsonAsync<List<AiExample>>(ExamplesPath, DefinitionSerializer.Options, cancellationToken) ?? [];
        }
        catch (Exception e) when (e is HttpRequestException or ExecutionRejectedException or JsonException
                                  || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, e);
            throw new AiUnavailableException(AiUnavailableException.DefaultMessage, e);
        }
    }

    public async Task<ControlSuggestion> SuggestAsync(SuggestRequest request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(SuggestPath, request, DefinitionSerializer.Options, cancellationToken);
        }
        catch (Exception e) when (e is HttpRequestException or ExecutionRejectedException   // timeout / open circuit
                                  || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, e);
            throw new AiUnavailableException(AiUnavailableException.DefaultMessage, e);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await ReadSuggestionAsync(response, cancellationToken);
            }

            var problem = await ReadProblemAsync(response, cancellationToken);
            LogFailed(logger, (int)response.StatusCode, problem?.Code, problem?.Detail);

            // The service validated the model's answer and rejected it - the model, not the service, is at fault.
            if (response.StatusCode == HttpStatusCode.BadGateway && problem?.Code == "invalidResponse")
            {
                throw new AiResponseInvalidException(AiResponseInvalidException.DefaultMessage);
            }
            throw new AiUnavailableException(AiUnavailableException.DefaultMessage);
        }
    }

    private async Task<ControlSuggestion> ReadSuggestionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ControlSuggestion>(DefinitionSerializer.Options, cancellationToken)
                ?? throw new JsonException("Empty suggestion.");
        }
        catch (JsonException e)
        {
            LogInvalid(logger, e);
            throw new AiResponseInvalidException(AiResponseInvalidException.DefaultMessage, e);
        }
    }

    private static async Task<AiProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<AiProblem>(ProblemJson, cancellationToken);
        }
        catch (JsonException)
        {
            return null;   // not a problem document - the status code alone decides
        }
    }

    private static readonly JsonSerializerOptions ProblemJson = new(JsonSerializerDefaults.Web);

    private sealed record AiProblem(string? Code, string? Detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI service unreachable or timed out")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI service answered {Status} ({Code}): {Detail}")]
    private static partial void LogFailed(ILogger logger, int status, string? code, string? detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI service answer does not match the contract")]
    private static partial void LogInvalid(ILogger logger, Exception exception);
}
