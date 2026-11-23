using Azure.Identity;
using Azure.Storage.Blobs;
using Klesia.Application.Abstractions;
using Klesia.Infrastructure.Documents;
using Klesia.Infrastructure.Health;
using Klesia.Infrastructure.Options;
using Klesia.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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
        services.AddScoped<IDemandRepository, DemandRepository>();
        services.AddSingleton(serviceProvider =>
        {
            var storage = serviceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new BlobServiceClient(
                new Uri(storage.BlobServiceUri, UriKind.Absolute),
                new DefaultAzureCredential());
        });
        services.AddSingleton<IBaeDocumentStore, AzureBaeDocumentStore>();
        services.AddSingleton<ISqlHealthProbe, SqlHealthProbe>();
        services.AddSingleton<IHealthCheck, SqlHealthCheck>();

        return services;
    }
}
