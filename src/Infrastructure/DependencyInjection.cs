using Klesia.Application.Abstractions;
using Klesia.Infrastructure.Health;
using Klesia.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Klesia.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddSingleton<ISqlHealthProbe, SqlHealthProbe>();
        services.AddSingleton<IHealthCheck, SqlHealthCheck>();

        return services;
    }
}
