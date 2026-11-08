using Application.Demands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApi.Authentication;
using WebApi.Contracts;

namespace WebApi.Controllers;

[ApiController]
[Route("api/v1/demands")]
[Authorize(Policy = AuthorizationPolicies.DelegatedUser)]
public sealed class DemandsController(DemandService demandService) : ControllerBase
{
    /// <summary>Returns the signed-in user's identity and only their own demands.</summary>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType<DemandsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DemandsResponse>> GetAsync(CancellationToken cancellationToken)
    {
        // The identity comes exclusively from the validated access token, never from query, body or headers.
        if (!Guid.TryParse(User.FindFirst("oid")?.Value, out Guid userUniqueId))
        {
            return Unauthorized();
        }

        UserDemandsDto result = await demandService.GetUserDemandsAsync(userUniqueId, cancellationToken);

        return Ok(new DemandsResponse(
            result.UserId,
            result.UserName,
            result.Demands
                .Select(demand => new DemandResponse(demand.DemandId, demand.SirenNumber, demand.BaeLink, demand.Status))
                .ToArray()));
    }
}
