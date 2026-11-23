using System.Net;
using Klesia.Application.Authorization;
using Klesia.Application.Documents;
using Klesia.Application.Notifications;
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

        if (exception is RealtimeNegotiationUnavailableException)
        {
            _logger.LogWarning(
                "Azure SignalR negotiation unavailable for {Method} {RouteTemplate}.",
                httpContext.Request.Method,
                GetRouteTemplate(httpContext));
            await Problems.WriteServiceUnavailableAsync(httpContext, GetCorrelationId(httpContext));
            return true;
        }

        var traceId = GetCorrelationId(httpContext);
        if (exception is DemandNotFoundException)
        {
            await Problems.WriteNotFoundAsync(httpContext, traceId);
            return true;
        }

        if (exception is DocumentStoreUnavailableException)
        {
            _logger.LogWarning("Document storage unavailable while processing {Method} {RouteTemplate}.",
                httpContext.Request.Method,
                GetRouteTemplate(httpContext));
            await Problems.WriteServiceUnavailableAsync(httpContext, traceId);
            return true;
        }

        if (exception is StoredDocumentIntegrityException integrityException)
        {
            _logger.LogError("Stored document integrity failure for demand {DemandId}.", integrityException.DemandId);
            await Problems.WriteInternalServerErrorAsync(httpContext, traceId);
            return true;
        }

        var routeTemplate = GetRouteTemplate(httpContext);

        _logger.LogError(exception, "Unhandled exception while processing {Method} {RouteTemplate}",
            httpContext.Request.Method,
            routeTemplate);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Problems.WriteInternalServerErrorAsync(httpContext, traceId);

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

    private static string GetRouteTemplate(HttpContext context) =>
        context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint endpoint
        && endpoint.RoutePattern.RawText is not null
            ? endpoint.RoutePattern.RawText
            : "<unmatched>";
}
