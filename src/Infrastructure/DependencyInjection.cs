using Application.Abstractions;
using Application.Demands;
using Infrastructure.Documents;
using Infrastructure.Options;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SqlServerOptions>()
            .Bind(configuration.GetSection(SqlServerOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.KlesiaDb) && !options.KlesiaDb.Contains('<', StringComparison.Ordinal),
                "ConnectionStrings:KlesiaDb is required and must not contain a '<placeholder>'. See docs/local-development.md.")
            .ValidateOnStart();

        services.AddOptions<AzureOptions>()
            .Bind(configuration.GetSection(AzureOptions.SectionName));

        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDemandRepository, DemandRepository>();
        services.AddSingleton<IBaeLinkResolver, HttpsBaeLinkResolver>();
        services.AddScoped<DemandService>();

        return services;
    }
}
