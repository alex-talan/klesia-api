namespace Klesia.Infrastructure.Options;

public sealed class DocumentIntelligenceOptions
{
    public const string SectionName = "DocumentIntelligence";

    public string Endpoint { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;

    public string? ApiVersion { get; set; }
}