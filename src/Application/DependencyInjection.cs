using Klesia.Application.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Klesia.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthorizedUserResolver>();
        return services;
    }
}
