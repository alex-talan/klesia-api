using Klesia.Application.Notifications;

namespace Klesia.Infrastructure.Notifications;

internal sealed class AzureSignalRNegotiator(IDemandsHubClient hubClient) : IRealtimeNegotiator
{
    public async Task<RealtimeNegotiation> NegotiateAsync(Guid userOid, CancellationToken cancellationToken)
    {
        try
        {
            var response = await hubClient.NegotiateAsync(userOid.ToString("D"), cancellationToken);
            if (!Uri.TryCreate(response.Url, UriKind.Absolute, out var url)
                || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(response.AccessToken))
            {
                throw new RealtimeNegotiationUnavailableException();
            }

            return new RealtimeNegotiation(url, response.AccessToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RealtimeNegotiationUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new RealtimeNegotiationUnavailableException();
        }
    }
}