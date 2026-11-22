using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;

namespace IntegrationTests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task MissingBearerTokenReturns401Problem()
    {
        await AssertUnauthorizedAsync(request => { });
    }

    [Theory]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    [InlineData("Bearer not-a-jwt")]
    public async Task MalformedBearerHeaderReturns401Problem(string authorization)
    {
        await AssertUnauthorizedAsync(request =>
            request.Headers.TryAddWithoutValidation("Authorization", authorization));
    }

    [Theory]
    [InlineData(TestTokenKind.Expired)]
    [InlineData(TestTokenKind.NotYetValid)]
    [InlineData(TestTokenKind.WrongSignature)]
    [InlineData(TestTokenKind.Unsigned)]
    [InlineData(TestTokenKind.WrongIssuer)]
    [InlineData(TestTokenKind.WrongAudience)]
    [InlineData(TestTokenKind.WrongTenant)]
    [InlineData(TestTokenKind.IdToken)]
    [InlineData(TestTokenKind.MissingOid)]
    [InlineData(TestTokenKind.NonGuidOid)]
    public async Task InvalidTokenKindsReturn401Problem(TestTokenKind kind)
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/authenticated");
        var token = factory.Tokens.Create(kind);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request);

        await AssertUnauthorizedResponseAsync(response);
    }

    [Fact]
    public async Task ValidAccessTokenPassesAuthentication()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/authenticated");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.Tokens.Create());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        correlationId.Should().NotBeNullOrWhiteSpace();
    }

    private static async Task AssertUnauthorizedAsync(Action<HttpRequestMessage> configure)
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/authenticated");
        configure(request);

        var response = await client.SendAsync(request);

        await AssertUnauthorizedResponseAsync(response);
    }

    private static async Task AssertUnauthorizedResponseAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        response.Headers.WwwAuthenticate.Single().Scheme.Should().Be("Bearer");

        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("title").GetString().Should().Be("Unauthorized");
        document.RootElement.GetProperty("status").GetInt32().Should().Be(401);
        document.RootElement.GetProperty("traceId").GetString().Should().Be(correlationId);
    }
}
