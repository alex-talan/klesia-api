using FluentAssertions;
using IntegrationTests.Support;

namespace IntegrationTests.Api;

/// <summary>Acceptance scenarios AT-13 and AT-14.</summary>
public sealed class CorsTests : IDisposable
{
    private readonly KlesiaWebApplicationFactory factory = new();

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task At13_PreflightFromApprovedOrigin_IsAllowed()
    {
        using HttpResponseMessage response = await PreflightAsync(KlesiaWebApplicationFactory.ApprovedOrigin);

        response.IsSuccessStatusCode.Should().BeTrue();
        HeaderValues(response, "Access-Control-Allow-Origin").Should().Equal(KlesiaWebApplicationFactory.ApprovedOrigin);
        string.Join(',', HeaderValues(response, "Access-Control-Allow-Headers")).Should().ContainEquivalentOf("authorization");
        string.Join(',', HeaderValues(response, "Access-Control-Allow-Methods")).Should().Contain("GET");
    }

    [Fact]
    public async Task At14_PreflightFromUnapprovedOrigin_IsNotGrantedCorsAccess()
    {
        using HttpResponseMessage response = await PreflightAsync("https://evil.example.com");

        HeaderValues(response, "Access-Control-Allow-Origin").Should().BeEmpty();
    }

    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Options, "/api/v1/demands");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return await client.SendAsync(request);
    }

    private static string[] HeaderValues(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.ToArray() : [];
}
