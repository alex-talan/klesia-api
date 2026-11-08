namespace WebApi.Authentication;

/// <summary>Microsoft Entra ID settings of the API (configuration section <c>AzureAd</c>).</summary>
public sealed class EntraIdOptions
{
    public const string SectionName = "AzureAd";

    /// <summary>Authority host, for example <c>https://login.microsoftonline.com/</c>.</summary>
    public string Instance { get; init; } = "https://login.microsoftonline.com/";

    /// <summary>The single tenant allowed to call the API (GUID).</summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Application (client) ID of the API app registration (GUID).</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Delegated scope the access token must carry in <c>scp</c>, without the <c>api://…/</c> prefix.</summary>
    public string RequiredScope { get; init; } = string.Empty;

    /// <summary>Optional accepted audiences. Defaults to <c>ClientId</c> and <c>api://ClientId</c>.</summary>
    public string[] Audiences { get; init; } = [];

    /// <summary>Issuer / authority of v2.0 tokens for the configured tenant.</summary>
    public string Authority => $"{Instance.TrimEnd('/')}/{TenantId}/v2.0";

    public string[] GetValidAudiences() => Audiences.Length > 0 ? Audiences : [ClientId, $"api://{ClientId}"];
}
