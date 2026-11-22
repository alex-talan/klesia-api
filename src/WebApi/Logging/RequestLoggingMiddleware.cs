using System.Diagnostics;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Klesia.WebApi.Logging;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<RequestLoggingMiddleware> logger)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            var routeTemplate = context.GetEndpoint() is RouteEndpoint endpoint
                && endpoint.RoutePattern.RawText is not null
                    ? endpoint.RoutePattern.RawText
                    : context.Request.Path.HasValue
                        ? "<unmatched>"
                        : "/";

            var correlationId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemName, out var value)
                ? value?.ToString()
                : context.TraceIdentifier;

            logger.LogInformation(
                "Request completed {Method} {RouteTemplate} {StatusCode} {ElapsedMs} {CorrelationId}",
                context.Request.Method,
                routeTemplate,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                correlationId);
        }
    }
}
