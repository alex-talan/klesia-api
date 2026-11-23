using System.Text.Json.Serialization;
using Klesia.Application.Notifications;

namespace Klesia.WebApi.Contracts;

/// <summary>Redirect negotiation response for an Azure SignalR client connection.</summary>
public sealed record SignalRNegotiationResponse(
    [property: JsonPropertyName("url")] Uri Url,
    [property: JsonPropertyName("accessToken")] string AccessToken)
{
    public static SignalRNegotiationResponse From(RealtimeNegotiation negotiation) =>
        new(negotiation.Url, negotiation.AccessToken);
}