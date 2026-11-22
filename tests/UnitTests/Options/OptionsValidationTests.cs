using FluentAssertions;
using Klesia.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace UnitTests.Configuration;

public sealed class OptionsValidationTests
{
    [Fact]
    public void AzureAdOptionsValidator_RejectsMissingAndPlaceholderValuesWithoutEchoingThem()
    {
        var options = new AzureAdOptions
        {
            TenantId = "<tenant-guid>",
            ClientId = "not-a-guid",
            RequiredScope = "api://<api-id>/access_as_user",
            Instance = "http://login.example.com/",
            Audiences = ["<audience>"]
        };

        var result = new AzureAdOptionsValidator().Validate(Options.DefaultName, options);

        result.Failures.Should().Contain(failure => failure.Contains("AzureAd:TenantId", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("AzureAd:ClientId", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("AzureAd:Instance", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("AzureAd:RequiredScope", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("AzureAd:Audiences", StringComparison.Ordinal));
        string.Join(" ", result.Failures ?? Array.Empty<string>()).Should().NotContain("tenant-guid").And.NotContain("api-id").And.NotContain("audience");
    }

    [Fact]
    public void AzureAdOptionsValidator_AcceptsValidV2Configuration()
    {
        var options = new AzureAdOptions
        {
            TenantId = "f47ac10b-58cc-4372-a567-0e02b2c3d479",
            ClientId = "f47ac10b-58cc-4372-a567-0e02b2c3d480",
            RequiredScope = "access_as_user"
        };

        new AzureAdOptionsValidator().Validate(Options.DefaultName, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com?query=1")]
    [InlineData("http://example.com")]
    [InlineData("http://localhost.evil")]
    [InlineData("http://127.0.0.2")]
    public void CorsOptionsValidator_RejectsInvalidOrigins(string origin)
    {
        var result = new CorsOptionsValidator().Validate(
            Options.DefaultName,
            new CorsOptions { AllowedOrigins = [origin] });

        result.Failures.Should().Contain(failure => failure.Contains("Cors:AllowedOrigins", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:5180")]
    [InlineData("https://web.example.com")]
    public void CorsOptionsValidator_AcceptsExactApprovedOriginShapes(string origin)
    {
        new CorsOptionsValidator().Validate(
            Options.DefaultName,
            new CorsOptions { AllowedOrigins = [origin] }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void CorsOptionsValidator_AllowsEmptyOrigins()
    {
        new CorsOptionsValidator().Validate(Options.DefaultName, new CorsOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("<connection-string>")]
    [InlineData("Server=sql.example;Database=klesia;User ID=user;Password=secret")]
    [InlineData("Server=sql.example;Database=klesia;User ID=user;Pwd=secret")]
    [InlineData("Server=sql.example;Database=klesia;Authentication=Active Directory Integrated;Encrypt=True")]
    [InlineData("not-a-connection-string")]
    public void DatabaseOptionsValidator_RejectsMissingPlaceholderPasswordOrMalformedConnection(string connectionString)
    {
        var result = new DatabaseOptionsValidator().Validate(
            Options.DefaultName,
            new DatabaseOptions { KlesiaDb = connectionString });

        result.Failures.Should().Contain(failure => failure.Contains("ConnectionStrings:KlesiaDb", StringComparison.Ordinal));
        string.Join(" ", result.Failures ?? Array.Empty<string>()).Should().NotContain("secret");
    }

    [Fact]
    public void DatabaseOptionsValidator_AcceptsPasswordlessConnectionString()
    {
        var options = new DatabaseOptions
        {
            KlesiaDb = "Server=tcp:sql.example,1433;Database=klesia;Authentication=Active Directory Default;Encrypt=True"
        };

        new DatabaseOptionsValidator().Validate(Options.DefaultName, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://storage.example.blob.core.windows.net", "bae")]
    [InlineData("https://storage.example.blob.core.windows.net/?sig=hidden", "bae")]
    [InlineData("https://storage.example.blob.core.windows.net", "UPPERCASE")]
    [InlineData("https://storage.example.blob.core.windows.net", "ab")]
    [InlineData("https://storage.example.blob.core.windows.net", "bad--name")]
    [InlineData("https://<storage>.blob.core.windows.net", "bae")]
    public void StorageOptionsValidator_RejectsInvalidEndpointOrContainer(string blobServiceUri, string containerName)
    {
        var result = new StorageOptionsValidator().Validate(
            Options.DefaultName,
            new StorageOptions { BlobServiceUri = blobServiceUri, ContainerName = containerName });

        result.Failed.Should().BeTrue();
        result.Failures.Should().NotBeEmpty();
    }

    [Fact]
    public void StorageOptionsValidator_AcceptsHttpsEndpointAndValidContainer()
    {
        var options = new StorageOptions
        {
            BlobServiceUri = "https://storage.example.blob.core.windows.net",
            ContainerName = "bae-documents"
        };

        new StorageOptionsValidator().Validate(Options.DefaultName, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UploadOptionsValidator_RejectsNonPositiveLimit(int maxPdfSizeMb)
    {
        var result = new UploadOptionsValidator().Validate(
            Options.DefaultName,
            new UploadOptions { MaxPdfSizeMb = maxPdfSizeMb });

        result.Failures.Should().Contain(failure => failure.Contains("Upload:MaxPdfSizeMb", StringComparison.Ordinal));
    }

    [Fact]
    public void UploadOptions_ConvertsMegabytesToBytes()
    {
        new UploadOptions { MaxPdfSizeMb = 10 }.MaxPdfSizeBytes.Should().Be(10L * 1_048_576);
    }

    [Fact]
    public void DocumentIntelligenceOptionsValidator_RejectsMissingPlaceholderAndInvalidTimeout()
    {
        var result = new DocumentIntelligenceOptionsValidator().Validate(
            Options.DefaultName,
            new DocumentIntelligenceOptions
            {
                Endpoint = "https://<resource>.cognitiveservices.azure.com/",
                ModelId = "",
                TimeoutSeconds = 0
            });

        result.Failures.Should().Contain(failure => failure.Contains("DocumentIntelligence:Endpoint", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("DocumentIntelligence:ModelId", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("DocumentIntelligence:TimeoutSeconds", StringComparison.Ordinal));
        string.Join(" ", result.Failures ?? Array.Empty<string>()).Should().NotContain("resource");
    }

    [Fact]
    public void DocumentIntelligenceOptionsValidator_RejectsPlaceholderApiVersion()
    {
        var result = new DocumentIntelligenceOptionsValidator().Validate(
            Options.DefaultName,
            new DocumentIntelligenceOptions
            {
                Endpoint = "https://di.example.cognitiveservices.azure.com/",
                ModelId = "bae-model",
                ApiVersion = "<api-version>"
            });

        result.Failures.Should().Contain(failure => failure.Contains("DocumentIntelligence:ApiVersion", StringComparison.Ordinal));
        string.Join(" ", result.Failures ?? Array.Empty<string>()).Should().NotContain("api-version");
    }

    [Fact]
    public void DocumentIntelligenceOptionsValidator_AcceptsValidConfiguration()
    {
        var options = new DocumentIntelligenceOptions
        {
            Endpoint = "https://di.example.cognitiveservices.azure.com/",
            ModelId = "bae-model",
            TimeoutSeconds = 120
        };

        new DocumentIntelligenceOptionsValidator().Validate(Options.DefaultName, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ExtractionOptionsValidator_RejectsInvalidBoundsAndInsufficientStaleAge()
    {
        var result = new ExtractionOptionsValidator(120).Validate(
            Options.DefaultName,
            new ExtractionOptions
            {
                MaxConcurrency = 0,
                QueueCapacity = 0,
                StaleAfterMinutes = 2,
                SweepIntervalMinutes = 0
            });

        result.Failures.Should().Contain(failure => failure.Contains("Extraction:MaxConcurrency", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("Extraction:QueueCapacity", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("Extraction:StaleAfterMinutes", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("Extraction:SweepIntervalMinutes", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractionOptionsValidator_AcceptsSpecificationDefaults()
    {
        new ExtractionOptionsValidator(120)
            .Validate(Options.DefaultName, new ExtractionOptions())
            .Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://signalr.example")]
    [InlineData("https://signalr.example/path")]
    [InlineData("https://<resource>.service.signalr.net")]
    public void AzureSignalROptionsValidator_RejectsInvalidEndpoint(string endpoint)
    {
        var result = new AzureSignalROptionsValidator().Validate(
            Options.DefaultName,
            new AzureSignalROptions { Endpoint = endpoint });

        result.Failures.Should().Contain(failure => failure.Contains("AzureSignalR:Endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void AzureSignalROptionsValidator_RejectsUnexpectedHubName()
    {
        var result = new AzureSignalROptionsValidator().Validate(
            Options.DefaultName,
            new AzureSignalROptions { Endpoint = "https://signalr.example", HubName = "other" });

        result.Failures.Should().Contain(failure => failure.Contains("AzureSignalR:HubName", StringComparison.Ordinal));
    }

    [Fact]
    public void AddKlesiaOptions_RegistersValidatorsAndDefersDocumentIntelligenceValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = "f47ac10b-58cc-4372-a567-0e02b2c3d479",
            ["AzureAd:ClientId"] = "f47ac10b-58cc-4372-a567-0e02b2c3d480",
            ["AzureAd:RequiredScope"] = "access_as_user",
            ["ConnectionStrings:KlesiaDb"] = "Server=tcp:sql.example,1433;Database=klesia;Authentication=Active Directory Default;Encrypt=True",
            ["Storage:BlobServiceUri"] = "https://storage.example.blob.core.windows.net",
            ["Storage:ContainerName"] = "bae-documents",
            ["AzureSignalR:Endpoint"] = "https://signalr.example",
            ["AzureSignalR:HubName"] = "demands"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddKlesiaOptions(configuration);

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IStartupValidator));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<AzureAdOptions>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<CorsOptions>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<DatabaseOptions>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<StorageOptions>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<UploadOptions>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<ExtractionOptions>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<AzureSignalROptions>));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<DocumentIntelligenceOptions>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IStartupValidator));

        services.AddDocumentIntelligenceOptions(configuration);

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IValidateOptions<DocumentIntelligenceOptions>));
    }
}