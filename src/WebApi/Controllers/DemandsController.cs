using Klesia.Application.Demands;
using Klesia.WebApi.Authentication;
using Klesia.WebApi.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Klesia.WebApi.Controllers;

[ApiController]
[Route("api/v1/demands")]
public sealed class DemandsController : ControllerBase
{
    private readonly DemandService _demandService;
    private readonly ICurrentPrincipal _currentPrincipal;

    public DemandsController(DemandService demandService, ICurrentPrincipal currentPrincipal)
    {
        _demandService = demandService;
        _currentPrincipal = currentPrincipal;
    }

    [HttpGet]
    [ProducesResponseType<UserDemandsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDemandsResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var oid = _currentPrincipal.GetOid(User);
        var result = await _demandService.ListAsync(oid, cancellationToken);

        return Ok(UserDemandsResponse.From(result));
    }
}