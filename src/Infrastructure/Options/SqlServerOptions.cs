namespace Infrastructure.Options;

public sealed class SqlServerOptions
{
    public const string SectionName = "ConnectionStrings";

    /// <summary>
    /// Azure SQL Database connection string. Use <c>Authentication=Active Directory Default</c> so that no
    /// secret is stored: it signs in with the developer's Azure CLI / Visual Studio account on a PC and
    /// with the managed identity in Azure.
    /// </summary>
    public string KlesiaDb { get; init; } = string.Empty;
}
