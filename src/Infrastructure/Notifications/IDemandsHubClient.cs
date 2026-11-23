namespace Klesia.Infrastructure.Notifications;

internal interface IDemandsHubClient
{
    public Task SendToUserAsync(
        string userId,
        string methodName,
        object?[] arguments,
        CancellationToken cancellationToken);

    public Task<SignalRNegotiation> NegotiateAsync(string userId, CancellationToken cancellationToken);
}

internal sealed record SignalRNegotiation(string? Url, string? AccessToken);