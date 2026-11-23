using Klesia.Application.Authorization;
using Klesia.Application.Demands;
using Klesia.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Klesia.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthorizedUserResolver>();
        services.AddScoped<DemandService>();
        services.AddScoped<NegotiateNotificationsService>();
        return services;
    }
}
