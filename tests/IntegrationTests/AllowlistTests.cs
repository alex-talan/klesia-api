using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;

namespace IntegrationTests;

public sealed class AllowlistTests
{
    [Fact]
    public async Task UnknownAndInactiveRejectWithIdenticalForbiddenBody()
    {
        using var unknownFactory = new KlesiaApiFactory();
        using var unknownClient = unknownFactory.CreateClient();
        using var unknownRequest = new HttpRequestMessage(HttpMethod.Get, "/test/allowlist-unknown");
        unknownRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", unknownFactory.Tokens.Create());

        var unknownResponse = await unknownClient.SendAsync(unknownRequest);

        using var inactiveFactory = new KlesiaApiFactory();
        using var inactiveClient = inactiveFactory.CreateClient();
        using var inactiveRequest = new HttpRequestMessage(HttpMethod.Get, "/test/allowlist-inactive");
        inactiveRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inactiveFactory.Tokens.Create());

        var inactiveResponse = await inactiveClient.SendAsync(inactiveRequest);

        unknownResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        inactiveResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var unknownBody = await GetBodyWithoutTraceIdAsync(unknownResponse);
        var inactiveBody = await GetBodyWithoutTraceIdAsync(inactiveResponse);
        unknownBody.Should().Be(inactiveBody);

        unknownResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        inactiveResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task RepositoryFailureReturns500ProblemNot403()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/allowlist-failure");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", factory.Tokens.Create());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("REPOSITORY_FAILURE_TEST_TEXT");
    }

    private static async Task<string> GetBodyWithoutTraceIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var title = root.GetProperty("title").GetString();
        var status = root.GetProperty("status").GetInt32();
        return $"{title}|{status}";
    }
}
