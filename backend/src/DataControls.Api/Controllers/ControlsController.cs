using DataControls.Application.Controls;
using DataControls.Application.Runs;
using DataControls.Core.Definitions;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>Saved controls: manage them, check a definition, run a control and browse its violations.</summary>
[ApiController]
[Route("api/controls")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class ControlsController(ControlService controls, ControlRunService runs) : ControllerBase
{
    /// <summary>Active controls ordered by name, optionally filtered by text in the name or description.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ControlListItemDto>>> List([FromQuery] string? search, CancellationToken cancellationToken) =>
        Ok(await controls.ListAsync(search, cancellationToken));

    /// <summary>One control with its definition and the Hebrew sentence it means.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ControlDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await controls.GetAsync(id, cancellationToken));

    /// <summary>Creates a control. The definition is validated against the catalog first.</summary>
    [HttpPost]
    [ProducesResponseType<ControlDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ControlDto>> Create(SaveControlRequest request, CancellationToken cancellationToken)
    {
        var created = await controls.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Updates a control (its last-run result is cleared - it no longer matches the new definition).</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ControlDto>> Update(int id, SaveControlRequest request, CancellationToken cancellationToken) =>
        Ok(await controls.UpdateAsync(id, request, cancellationToken));

    /// <summary>Deletes a control (soft delete: kept in the database, never shown again).</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await controls.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Checks an unsaved definition: the Hebrew sentence it means, or the errors with the path of each offending node.</summary>
    [HttpPost("validate")]
    public async Task<ActionResult<ValidateDefinitionResponse>> Validate(ControlDefinition definition, CancellationToken cancellationToken) =>
        Ok(await controls.ValidateAsync(definition, cancellationToken));

    /// <summary>Runs the control now and records the outcome; returns the first page of violations with reasons.</summary>
    [HttpPost("{id:int}/run")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<ActionResult<RunResultDto>> Run(int id, CancellationToken cancellationToken) =>
        Ok(await runs.RunAsync(id, cancellationToken));

    /// <summary>A page of the control's violations with search, column filters and sorting (does not record a run).</summary>
    [HttpPost("{id:int}/results")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<RunResultDto>> Results(int id, ResultsRequest request, CancellationToken cancellationToken) =>
        Ok(await runs.GetResultsAsync(id, request, cancellationToken));
}
