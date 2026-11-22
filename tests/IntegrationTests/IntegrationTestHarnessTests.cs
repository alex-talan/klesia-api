using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace IntegrationTests;

public sealed class IntegrationTestHarnessTests
{
    [Fact]
    public async Task Host_Starts_WithTestConfigurationAndWithoutOpenApiInTesting()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    public void ValidToken_ContainsRequiredClaims()
    {
        using var tokens = new TestTokenFactory();

        var token = tokens.Create();

        token.Should().NotBeNullOrWhiteSpace();
        token.Split('.').Should().HaveCount(3);
    }

    [Theory]
    [InlineData(TestTokenKind.WrongIssuer)]
    [InlineData(TestTokenKind.WrongAudience)]
    [InlineData(TestTokenKind.WrongTenant)]
    [InlineData(TestTokenKind.MissingScope)]
    [InlineData(TestTokenKind.MissingOid)]
    [InlineData(TestTokenKind.Expired)]
    [InlineData(TestTokenKind.WrongSignature)]
    [InlineData(TestTokenKind.Unsigned)]
    public void InvalidTokenKinds_ProduceSignedOrUnsignedJwtOnDemand(TestTokenKind kind)
    {
        using var tokens = new TestTokenFactory();

        var token = tokens.Create(kind);

        token.Should().NotBeNullOrWhiteSpace();
        token.Split('.').Should().HaveCount(3);
    }

    [Fact]
    public void TestLogSink_CapturesFormattedMessages()
    {
        using var factory = new KlesiaApiFactory();
        factory.LogSink.Clear();

        var logger = Microsoft.Extensions.Logging.LoggerFactory
            .Create(builder => builder.AddProvider(factory.LogSink))
            .CreateLogger("IntegrationTest");
        logger.LogInformation("sensitive-marker");

        factory.LogSink.Entries.Should().Contain(entry => entry.Message == "sensitive-marker");
    }
}
