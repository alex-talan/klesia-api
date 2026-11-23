using FluentAssertions;
using Klesia.Application.Abstractions;
using Klesia.Application.Demands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IntegrationTests;

public sealed class ApplicationLoggingResolutionTests
{
    [Fact]
    public void ApiHostResolvesLoggerForApplicationDemandService()
    {
        using var factory = new KlesiaApiFactory();

        var logger = factory.Services.GetRequiredService<ILogger<DemandService>>();

        logger.Should().NotBeNull();
    }

    [Fact]
    public void ApiHostResolvesDemandCreationSettingsFromUploadOptions()
    {
        using var factory = new KlesiaApiFactory();

        var settings = factory.Services.GetRequiredService<IDemandCreationSettings>();

        settings.MaxPdfSizeBytes.Should().Be(10L * 1_048_576);
    }
}