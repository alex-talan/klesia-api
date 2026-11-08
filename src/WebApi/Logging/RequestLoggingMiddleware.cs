using System.Diagnostics;

namespace WebApi.Logging;

/// <summary>
/// Structured request log: method, route, status code, duration and a correlation id.
/// Never logs the query string, headers, tokens or bodies.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public const string CorrelationHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = ResolveCorrelationId(context);
        context.Response.OnStarting(() =>
        {
            // Set when the response starts: the exception handler clears headers written earlier.
            context.Response.Headers[CorrelationHeader] = correlationId;
            return Task.CompletedTask;
        });

        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:0.0} ms",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        string? incoming = context.Request.Headers[CorrelationHeader].FirstOrDefault();

        bool isSafe = incoming is { Length: > 0 and <= 64 }
            && incoming.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

        return isSafe ? incoming! : context.TraceIdentifier;
    }
}
