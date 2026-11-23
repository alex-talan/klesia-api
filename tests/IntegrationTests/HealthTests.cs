using FluentAssertions;
using Klesia.Infrastructure.Health;
using Klesia.WebApi.Health;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IntegrationTests;

public sealed class HealthTests
{
    [Fact]
    public async Task HealthyCheckProducesOkExactBody()
    {
        var check = new SqlHealthCheck(new HealthySqlHealthProbe());
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Healthy);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await HealthResponseWriter.WriteAsync(context, HealthyReport());

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        (await reader.ReadToEndAsync()).Should().Be("{\"status\":\"ok\"}");
    }

    [Fact]
    public async Task UnhealthyCheckProducesUnavailableExactBody()
    {
        var check = new SqlHealthCheck(new UnhealthySqlHealthProbe());
        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.Should().Be(HealthStatus.Unhealthy);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await HealthResponseWriter.WriteAsync(context, UnhealthyReport());

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();
        body.Should().Be("{\"status\":\"unavailable\"}");
        body.Should().NotContain("TEST_SQL_UNAVAILABLE");
    }

    private static HealthReport HealthyReport() => new(
        new Dictionary<string, HealthReportEntry>
        {
            ["sql"] = new HealthReportEntry(HealthStatus.Healthy, null, TimeSpan.Zero, null, new Dictionary<string, object>(), [])
        },
        HealthStatus.Healthy,
        TimeSpan.Zero);

    private static HealthReport UnhealthyReport() => new(
        new Dictionary<string, HealthReportEntry>
        {
            ["sql"] = new HealthReportEntry(HealthStatus.Unhealthy, null, TimeSpan.Zero, null, new Dictionary<string, object>(), [])
        },
        HealthStatus.Unhealthy,
        TimeSpan.Zero);

    private sealed class HealthySqlHealthProbe : ISqlHealthProbe
    {
        public Task ProbeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnhealthySqlHealthProbe : ISqlHealthProbe
    {
        public Task ProbeAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("TEST_SQL_UNAVAILABLE");
    }
}
