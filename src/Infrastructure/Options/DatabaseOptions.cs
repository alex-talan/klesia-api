namespace Klesia.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string KlesiaDb { get; set; } = string.Empty;
}