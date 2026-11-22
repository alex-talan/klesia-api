namespace Klesia.Infrastructure.Options;

public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    public int MaxPdfSizeMb { get; set; } = 10;

    public long MaxPdfSizeBytes => (long)MaxPdfSizeMb * 1_048_576;
}