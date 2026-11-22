using Klesia.Infrastructure.Options;

namespace Klesia.WebApi.Cors;

public static class CorsServiceCollectionExtensions
{
    public const string PolicyName = "Klesia";

    public static IServiceCollection AddKlesiaCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var cors = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()
            ?? new CorsOptions();

        services.AddCors(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                policy.WithOrigins(cors.AllowedOrigins)
                    .WithMethods("GET", "POST", "OPTIONS")
                    .WithHeaders("Authorization", "Content-Type", "X-Requested-With", "X-SignalR-User-Agent")
                    .WithExposedHeaders("X-Correlation-Id")
                    .SetPreflightMaxAge(TimeSpan.FromSeconds(600));
            });
        });

        return services;
    }
}
