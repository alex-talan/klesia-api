using FluentAssertions;
using Infrastructure.Documents;
using Microsoft.Extensions.Options;
using WebApi.Authentication;

namespace IntegrationTests.Configuration;

public sealed class ConfigurationValidationTests
{
    private static readonly EntraIdOptionsValidator Validator = new();

    [Fact]
    public void EntraOptions_WithPlaceholders_AreRejected()
    {
        EntraIdOptions options = new() { TenantId = "<your-tenant-id>", ClientId = "<your-api-client-id>", RequiredScope = "<scope>" };

        ValidateOptionsResult result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(3);
    }

    [Fact]
    public void EntraOptions_WithRealLookingValues_AreAccepted()
    {
        EntraIdOptions options = new()
        {
            TenantId = Guid.NewGuid().ToString(),
            ClientId = Guid.NewGuid().ToString(),
            RequiredScope = "access_as_user"
        };

        Validator.Validate(null, options).Succeeded.Should().BeTrue();
        options.Authority.Should().Be($"https://login.microsoftonline.com/{options.TenantId}/v2.0");
        options.GetValidAudiences().Should().Equal(options.ClientId, $"api://{options.ClientId}");
    }

    [Theory]
    [InlineData("https://contoso.sharepoint.com/sites/klesia/Shared%20Documents/bae.pdf")]
    public void BaeLinkResolver_ReturnsValidHttpsLinkUnchanged(string link)
    {
        new HttpsBaeLinkResolver().Resolve(link).Should().Be(link);
    }

    [Theory]
    [InlineData("http://contoso.sharepoint.com/bae.pdf")]
    [InlineData("ftp://contoso.sharepoint.com/bae.pdf")]
    [InlineData("/relative/bae.pdf")]
    [InlineData("")]
    public void BaeLinkResolver_RejectsAnythingButAbsoluteHttps(string link)
    {
        Action act = () => new HttpsBaeLinkResolver().Resolve(link);

        act.Should().Throw<InvalidOperationException>();
    }
}
