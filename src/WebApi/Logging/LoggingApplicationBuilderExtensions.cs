namespace Klesia.WebApi.Logging;

public static class LoggingApplicationBuilderExtensions
{
    public static IApplicationBuilder UseKlesiaLogging(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        return app;
    }
}
