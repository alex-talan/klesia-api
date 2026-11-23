using Klesia.Application.Notifications;
using Klesia.WebApi.Authentication;
using Klesia.WebApi.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Klesia.WebApi.Controllers;

[ApiController]
[Route("api/v1/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly NegotiateNotificationsService _negotiateNotificationsService;
    private readonly ICurrentPrincipal _currentPrincipal;

    public NotificationsController(
        NegotiateNotificationsService negotiateNotificationsService,
        ICurrentPrincipal currentPrincipal)
    {
        ArgumentNullException.ThrowIfNull(negotiateNotificationsService);
        ArgumentNullException.ThrowIfNull(currentPrincipal);

        _negotiateNotificationsService = negotiateNotificationsService;
        _currentPrincipal = currentPrincipal;
    }

    [HttpPost("negotiate")]
    [EndpointName("negotiateNotifications")]
    [ProducesResponseType<SignalRNegotiationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SignalRNegotiationResponse>> NegotiateAsync(
        [FromQuery(Name = "negotiateVersion")] int? negotiateVersion,
        CancellationToken cancellationToken)
    {
        _ = negotiateVersion;
        var oid = _currentPrincipal.GetOid(User);
        var negotiation = await _negotiateNotificationsService.NegotiateAsync(oid, cancellationToken);

        return Ok(SignalRNegotiationResponse.From(negotiation));
    }
}