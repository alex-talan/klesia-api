namespace Klesia.Infrastructure.Health;

public interface ISqlHealthProbe
{
    public Task ProbeAsync(CancellationToken cancellationToken);
}
