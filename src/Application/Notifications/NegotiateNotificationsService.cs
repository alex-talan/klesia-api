using Klesia.Application.Authorization;

namespace Klesia.Application.Notifications;

public sealed class NegotiateNotificationsService
{
    private readonly AuthorizedUserResolver _authorizedUserResolver;
    private readonly IRealtimeNegotiator _realtimeNegotiator;

    public NegotiateNotificationsService(
        AuthorizedUserResolver authorizedUserResolver,
        IRealtimeNegotiator realtimeNegotiator)
    {
        ArgumentNullException.ThrowIfNull(authorizedUserResolver);
        ArgumentNullException.ThrowIfNull(realtimeNegotiator);

        _authorizedUserResolver = authorizedUserResolver;
        _realtimeNegotiator = realtimeNegotiator;
    }

    public async Task<RealtimeNegotiation> NegotiateAsync(Guid oid, CancellationToken cancellationToken)
    {
        var user = await _authorizedUserResolver.ResolveAsync(oid, cancellationToken);
        return await _realtimeNegotiator.NegotiateAsync(user.UserUniqueId, cancellationToken);
    }
}