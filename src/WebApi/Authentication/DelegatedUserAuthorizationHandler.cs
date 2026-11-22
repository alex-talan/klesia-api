using Klesia.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;

namespace Klesia.WebApi.Authentication;

public sealed class DelegatedUserRequirement : IAuthorizationRequirement
{
}

public sealed class DelegatedUserAuthorizationHandler
    : AuthorizationHandler<DelegatedUserRequirement>
{
    private readonly AzureAdOptions _azureAd;

    public DelegatedUserAuthorizationHandler(Microsoft.Extensions.Options.IOptions<AzureAdOptions> azureAd)
    {
        _azureAd = azureAd.Value;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        DelegatedUserRequirement requirement)
    {
        var scopeClaim = context.User.FindFirst("scp")?.Value;
        if (scopeClaim is not null
            && scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(_azureAd.RequiredScope, StringComparer.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
