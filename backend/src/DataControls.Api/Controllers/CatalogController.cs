using DataControls.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace DataControls.Api.Controllers;

/// <summary>The business catalog and operator rules - everything the UI needs to build a control in business terms.</summary>
[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(CatalogService catalog) : ControllerBase
{
    /// <summary>Business entities with their fields and relations (business names only).</summary>
    [HttpGet("entities")]
    public async Task<ActionResult<IReadOnlyList<EntityDto>>> GetEntities(CancellationToken cancellationToken) =>
        Ok(await catalog.GetEntitiesAsync(cancellationToken));

    /// <summary>Operators with Hebrew labels, the data types they apply to and the operand they take.</summary>
    [HttpGet("operators")]
    public ActionResult<IReadOnlyList<OperatorDto>> GetOperators() => Ok(CatalogService.GetOperators());

    /// <summary>The limits of a control (conditions, nesting, relation path, text lengths) - enforced by the server.</summary>
    [HttpGet("limits")]
    public ActionResult<LimitsDto> GetLimits() => Ok(catalog.GetLimits());
}
