using System.Net;
using FluentAssertions;

namespace IntegrationTests;

public sealed class HttpsTests
{
    [Fact]
    public async Task ForwardedHttpsRequestIsHttpsAndDoesNotRedirect()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/https");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var scheme = await response.Content.ReadAsStringAsync();
        scheme.Should().Be("https");
        response.StatusCode.Should().NotBe(HttpStatusCode.Redirect);
        response.StatusCode.Should().NotBe(HttpStatusCode.MovedPermanently);
    }
}
