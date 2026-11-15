using DataControls.Application.Ai;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>Stage B: a control described in free text, suggested by the AI for the user's approval.</summary>
[ApiController]
[Route("api/ai")]
public sealed class AiController(AiSuggestionService suggestions) : ControllerBase
{
    /// <summary>Requests offered as one-click examples, served by the AI service (the ones it answers).</summary>
    /// <summary>
    /// Free text → a suggested control: the AI's explanation and assumptions, the translated definition and its
    /// validation (the Hebrew meaning, or what is wrong). Nothing is saved or run - the user approves first.
    /// </summary>
    [HttpGet("examples")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<AiExample>>> GetExamples(CancellationToken cancellationToken) =>
        Ok(await suggestions.GetExamplesAsync(cancellationToken));

    [HttpPost("suggest-control")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<AiSuggestionResult>> SuggestControl(AiSuggestRequest request, CancellationToken cancellationToken) =>
        Ok(await suggestions.SuggestAsync(request, cancellationToken));
}
