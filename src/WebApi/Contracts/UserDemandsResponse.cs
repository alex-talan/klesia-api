using System.Text.Json.Serialization;
using Klesia.Application.Demands;

namespace Klesia.WebApi.Contracts;

/// <summary>Response containing the signed-in user's identity and demands.</summary>
public sealed record UserDemandsResponse(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("demands")] IReadOnlyList<DemandResponse> Demands)
{
    public static UserDemandsResponse From(UserDemandsResult result)
    {
        return new UserDemandsResponse(
            result.UserId.ToString("D"),
            result.UserName,
            result.Demands.Select(DemandResponse.From).ToArray());
    }
}

/// <summary>A demand in the signed-in user's demand list.</summary>
public sealed record DemandResponse(
    [property: JsonPropertyName("demand_id")] int DemandId,
    [property: JsonPropertyName("gk_number")] string GKNumber,
    [property: JsonPropertyName("bae_link")] string BaeLink,
    [property: JsonPropertyName("status")] string Status)
{
    public static DemandResponse From(DemandListItem demand)
    {
        return new DemandResponse(
            demand.DemandId,
            demand.GKNumber,
            DemandDocumentPath.For(demand.DemandId),
            demand.StatusCode);
    }
}

internal static class DemandDocumentPath
{
    public static string For(int demandId) => $"/api/v1/demands/{demandId}/bae";
}