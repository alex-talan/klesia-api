namespace Klesia.Infrastructure.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string BlobServiceUri { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;
}