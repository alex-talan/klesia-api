using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Klesia.Infrastructure.Health;

public sealed class SqlHealthCheck : IHealthCheck
{
    private readonly ISqlHealthProbe _probe;

    public SqlHealthCheck(ISqlHealthProbe probe)
    {
        _probe = probe;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _probe.ProbeAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
