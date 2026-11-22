using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;

namespace IntegrationTests;

public sealed class AuthorizationTests
{
    [Fact]
    public async Task MissingScopeReturns403Problem()
    {
        await AssertForbiddenAsync(TestTokenKind.MissingScope);
    }

    [Fact]
    public async Task OtherScopeReturns403Problem()
    {
        await AssertForbiddenAsync(TestTokenKind.OtherScope);
    }

    [Fact]
    public async Task RolesOnlyApplicationTokenReturns403Problem()
    {
        await AssertForbiddenAsync(TestTokenKind.RolesOnly);
    }

    [Fact]
    public async Task ExactScopeInSpaceSeparatedClaimAllowsEndpoint()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/authenticated");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.Tokens.Create(TestTokenKind.ScopeAndDelegatedScope));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownApiRouteWithoutTokenFallsBackTo401()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.WwwAuthenticate.Single().Scheme.Should().Be("Bearer");
    }

    private static async Task AssertForbiddenAsync(TestTokenKind kind)
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/authenticated");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.Tokens.Create(kind));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("title").GetString().Should().Be("Forbidden");
        document.RootElement.GetProperty("status").GetInt32().Should().Be(403);
        document.RootElement.TryGetProperty("traceId", out var traceId).Should().BeTrue();
        traceId.GetString().Should().Be(response.Headers.GetValues("X-Correlation-Id").Single());
    }
}
