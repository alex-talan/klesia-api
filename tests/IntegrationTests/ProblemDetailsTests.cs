using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace IntegrationTests;

public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task UnknownRouteReturnsProblemDetailsWithMatchingTraceId()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/unknown");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("title").GetString().Should().Be("Not Found");
        document.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        traceId.Should().Be(response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task ExceptionReturnsSanitizedProblemDetails()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/test/throw");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("SENSITIVE_TEST_EXCEPTION_TEXT");
        body.Should().NotContain("StackTrace");

        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
        document.RootElement.GetProperty("status").GetInt32().Should().Be(500);
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        traceId.Should().Be(response.Headers.GetValues("X-Correlation-Id").Single());
    }
}
