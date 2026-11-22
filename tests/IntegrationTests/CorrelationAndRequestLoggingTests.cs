using System.Net;
using FluentAssertions;
using Klesia.WebApi.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IntegrationTests;

public sealed class CorrelationAndRequestLoggingTests
{
    [Fact]
    public async Task ResponseContainsGeneratedCorrelationId()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.Headers.Should().Contain(header => header.Key == "X-Correlation-Id");
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        correlationId.Should().MatchRegex("^[A-Za-z0-9_.-]{32}$");
    }

    [Fact]
    public async Task SafeIncomingCorrelationIdIsEchoed()
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        request.Headers.Add("X-Correlation-Id", "test-correlation_123");

        var response = await client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single()
            .Should().Be("test-correlation_123");
    }

    [Theory]
    [InlineData("has spaces")]
    [InlineData("line1\rline2")]
    [InlineData("abcdefghijklmnopqrstuvwxyABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-extra")]
    public async Task UnsafeIncomingCorrelationIdIsReplaced(string unsafeId)
    {
        using var factory = new KlesiaApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/openapi/v1.json");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", unsafeId).Should().BeTrue();

        var response = await client.SendAsync(request);

        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        correlationId.Should().NotBe(unsafeId);
        correlationId.Should().MatchRegex("^[A-Za-z0-9_.-]{32}$");
    }

    [Fact]
    public async Task RequestLogHasRedactedStructuredFieldsAndNoAuthorization()
    {
        using var sink = new TestLogSink();
        sink.Clear();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(sink));
        var logger = loggerFactory.CreateLogger<RequestLoggingMiddleware>();

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/openapi/v1.json";
        context.Request.Headers["Authorization"] = "Bearer super-secret-token";
        context.Items[CorrelationIdMiddleware.ItemName] = "redaction-test";
        context.Response.StatusCode = StatusCodes.Status404NotFound;

        var middleware = new RequestLoggingMiddleware(nextContext =>
        {
            nextContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, logger);

        var entries = sink.Entries;
        entries.Should().Contain(entry =>
            entry.Message.Contains("GET")
            && entry.Message.Contains("404")
            && entry.Message.Contains("redaction-test"));
        entries.Should().OnlyContain(entry => !entry.Message.Contains("super-secret-token"));
        entries.Should().OnlyContain(entry => !entry.Message.Contains("GK260902-144743112"));
    }
}
