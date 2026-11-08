using System.Text.Json.Serialization;

namespace WebApi.Contracts;

/// <summary>Response of <c>GET /api/v1/demands</c>. Property names are part of the frontend contract (snake_case).</summary>
public sealed record DemandsResponse(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("demands")] IReadOnlyList<DemandResponse> Demands);

public sealed record DemandResponse(
    [property: JsonPropertyName("demand_id")] int DemandId,
    [property: JsonPropertyName("siren_number")] string SirenNumber,
    [property: JsonPropertyName("bae_link")] string BaeLink,
    [property: JsonPropertyName("status")] string Status);
