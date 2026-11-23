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
    private readonly ILogger<DemandsController> _logger;

    public DemandsController(
        DemandService demandService,
        ICurrentPrincipal currentPrincipal,
        ILogger<DemandsController> logger)
    {
        _demandService = demandService;
        _currentPrincipal = currentPrincipal;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType<UserDemandsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDemandsResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var oid = _currentPrincipal.GetOid(User);
        var result = await _demandService.ListAsync(oid, cancellationToken);

        return Ok(UserDemandsResponse.From(result));
    }

    [HttpGet("{demand_id:int:min(1)}/bae")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocumentAsync(
        [FromRoute(Name = "demand_id")] int demandId,
        CancellationToken cancellationToken)
    {
        var oid = _currentPrincipal.GetOid(User);
        var document = await _demandService.OpenDocumentAsync(oid, demandId, cancellationToken);

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/pdf";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"bae-{demandId}.pdf\"";
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (document.Length is not null)
        {
            Response.ContentLength = document.Length;
        }

        try
        {
            await document.Content.CopyToAsync(Response.Body, 81920, HttpContext.RequestAborted);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            if (Response.HasStarted)
            {
                _logger.LogWarning("Document stream canceled for demand {DemandId}.", demandId);
                HttpContext.Abort();
            }
        }
        catch (Exception) when (Response.HasStarted)
        {
            _logger.LogError("Document stream failed after response start for demand {DemandId}.", demandId);
            HttpContext.Abort();
        }
        finally
        {
            await document.Content.DisposeAsync();
        }

        return new EmptyResult();
    }
}