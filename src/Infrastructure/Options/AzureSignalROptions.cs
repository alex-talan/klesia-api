namespace Klesia.Infrastructure.Options;

public sealed class AzureSignalROptions
{
    public const string SectionName = "AzureSignalR";

    public string Endpoint { get; set; } = string.Empty;

    public string HubName { get; set; } = "demands";
}