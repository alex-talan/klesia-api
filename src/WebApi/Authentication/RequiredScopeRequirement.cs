using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace WebApi.Authentication;

/// <summary>Requires the configured delegated scope in the <c>scp</c> claim; app-only tokens have none and are denied.</summary>
public sealed class RequiredScopeRequirement : IAuthorizationRequirement;

public sealed class RequiredScopeHandler(IOptions<EntraIdOptions> options) : AuthorizationHandler<RequiredScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RequiredScopeRequirement requirement)
    {
        string requiredScope = options.Value.RequiredScope;

        bool hasScope = context.User
            .FindAll("scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);

        if (hasScope)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
