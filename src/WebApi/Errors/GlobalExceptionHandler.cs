using System.Net;
using Klesia.Application.Authorization;
using Microsoft.AspNetCore.Diagnostics;

namespace Klesia.WebApi.Errors;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is AccessDeniedException)
        {
            var forbiddenTraceId = GetCorrelationId(httpContext);
            await Problems.WriteForbiddenAsync(httpContext, forbiddenTraceId);
            return true;
        }

        var routeTemplate = httpContext.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint endpoint
            && endpoint.RoutePattern.RawText is not null
                ? endpoint.RoutePattern.RawText
                : "<unmatched>";

        _logger.LogError(exception, "Unhandled exception while processing {Method} {RouteTemplate}",
            httpContext.Request.Method,
            routeTemplate);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var traceId = GetCorrelationId(httpContext);

        var result = Results.Json(new
        {
            title = "An unexpected error occurred.",
            status = StatusCodes.Status500InternalServerError,
            traceId
        }, contentType: "application/problem+json", statusCode: StatusCodes.Status500InternalServerError);

        await result.ExecuteAsync(httpContext);

        return true;
    }

    private static string GetCorrelationId(HttpContext context)
    {
        if (context.Items.TryGetValue(Klesia.WebApi.Logging.CorrelationIdMiddleware.ItemName, out var value)
            && value is not null)
        {
            return value.ToString()!;
        }

        return context.TraceIdentifier;
    }
}
