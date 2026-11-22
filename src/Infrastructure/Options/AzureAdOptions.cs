namespace Klesia.Infrastructure.Options;

public sealed class AzureAdOptions
{
    public const string SectionName = "AzureAd";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string RequiredScope { get; set; } = string.Empty;

    public string[]? Audiences { get; set; } = [];
}