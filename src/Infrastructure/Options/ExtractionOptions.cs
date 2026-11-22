namespace Klesia.Infrastructure.Options;

public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    public int MaxConcurrency { get; set; } = 2;

    public int QueueCapacity { get; set; } = 100;

    public int StaleAfterMinutes { get; set; } = 15;

    public int SweepIntervalMinutes { get; set; } = 5;
}