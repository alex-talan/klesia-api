using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace WebApi.Cors;

public sealed class FrontendCorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Exact allowed frontend origins (scheme://host[:port], no path, never '*').</summary>
    public string[] AllowedOrigins { get; init; } = [];
}

public static class FrontendCorsExtensions
{
    public const string PolicyName = "Frontend";

    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FrontendCorsOptions>()
            .Bind(configuration.GetSection(FrontendCorsOptions.SectionName))
            .Validate(
                options => options.AllowedOrigins.All(IsValidOrigin),
                "Cors:AllowedOrigins must list exact origins (scheme://host[:port], no path); HTTPS is mandatory except for localhost; '*' is forbidden.")
            .ValidateOnStart();

        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<FrontendCorsOptions>>((cors, frontend) =>
                cors.AddPolicy(PolicyName, policy => policy
                    .WithOrigins(frontend.Value.AllowedOrigins)
                    .WithMethods("GET", "POST", "OPTIONS")
                    .WithHeaders("Authorization", "Content-Type")));

        return services;
    }

    private static bool IsValidOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        bool isHttps = uri.Scheme == Uri.UriSchemeHttps;
        bool isLocalHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;

        return (isHttps || isLocalHttp)
            && string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase);
    }
}
