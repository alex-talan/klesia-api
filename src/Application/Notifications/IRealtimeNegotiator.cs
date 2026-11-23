namespace Klesia.Application.Notifications;

public interface IRealtimeNegotiator
{
    public Task<RealtimeNegotiation> NegotiateAsync(Guid userOid, CancellationToken cancellationToken);
}

public sealed record RealtimeNegotiation(Uri Url, string AccessToken);

public sealed class RealtimeNegotiationUnavailableException : Exception
{
    public RealtimeNegotiationUnavailableException()
        : base("Azure SignalR negotiation is unavailable.")
    {
    }
}