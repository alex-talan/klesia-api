using System.Net;
using FluentAssertions;

namespace IntegrationTests;

public sealed class CorsTests
{
    private const string AllowedOrigin = "http://localhost:5173";

    [Fact]
    public async Task DemandPreflightAllowsConfiguredOriginAndAuthorization()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = CreatePreflight("/api/v1/demands", AllowedOrigin, "Authorization");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(AllowedOrigin);
        response.Headers.GetValues("Access-Control-Allow-Headers").Single().Should().Contain("Authorization");
        response.Headers.GetValues("Access-Control-Max-Age").Single().Should().Be("600");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Headers").Should().BeTrue();
    }

    [Fact]
    public async Task NegotiatePreflightAllowsSignalRHeaders()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = CreatePreflight(
            "/api/v1/notifications/negotiate",
            AllowedOrigin,
            "Authorization, X-Requested-With, X-SignalR-User-Agent",
            "POST");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(AllowedOrigin);
        var allowedHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").Single();
        allowedHeaders.Should().Contain("X-Requested-With");
        allowedHeaders.Should().Contain("X-SignalR-User-Agent");
        response.Headers.GetValues("Access-Control-Allow-Methods").Single().Should().Contain("POST");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task DocumentPreflightAllowsAuthorization()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = CreatePreflight("/api/v1/demands/1/bae", AllowedOrigin, "Authorization");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(AllowedOrigin);
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task EvilOriginIsRejected()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = CreatePreflight("/api/v1/demands", "https://evil.example", "Authorization");

        var response = await client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    [Fact]
    public async Task AllowedGetExposesCorrelationHeader()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        request.Headers.Add("Origin", AllowedOrigin);

        var response = await client.SendAsync(request);

        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
        response.Headers.GetValues("Access-Control-Allow-Origin").Single().Should().Be(AllowedOrigin);
        response.Headers.GetValues("Access-Control-Expose-Headers").Single().Should().Contain("X-Correlation-Id");
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
    }

    private static HttpRequestMessage CreatePreflight(
        string path,
        string origin,
        string requestedHeaders,
        string requestedMethod = "GET")
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", requestedMethod);
        request.Headers.Add("Access-Control-Request-Headers", requestedHeaders);
        return request;
    }
}
