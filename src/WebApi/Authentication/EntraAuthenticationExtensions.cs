using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace WebApi.Authentication;

public static class EntraAuthenticationExtensions
{
    public static IServiceCollection AddEntraAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EntraIdOptions>()
            .Bind(configuration.GetSection(EntraIdOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EntraIdOptions>, EntraIdOptionsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<EntraIdOptions>>((jwt, entra) => ConfigureJwtBearer(jwt, entra.Value));

        services.AddSingleton<IAuthorizationHandler, RequiredScopeHandler>();

        AuthorizationPolicy delegatedUserPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new RequiredScopeRequirement())
            .Build();

        // Every endpoint requires the policy unless it is explicitly marked [AllowAnonymous].
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.DelegatedUser, delegatedUserPolicy)
            .SetFallbackPolicy(delegatedUserPolicy);

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions jwt, EntraIdOptions entra)
    {
        jwt.Authority = entra.Authority;
        jwt.RequireHttpsMetadata = true;
        jwt.MapInboundClaims = false; // keep raw claim names: oid, scp, tid

        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = entra.Authority,
            ValidateAudience = true,
            ValidAudiences = entra.GetValidAudiences(),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = "name"
        };

        jwt.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                ValidateIdentityClaims(context, entra);
                return Task.CompletedTask;
            }
        };
    }

    private static void ValidateIdentityClaims(TokenValidatedContext context, EntraIdOptions entra)
    {
        ClaimsPrincipal? principal = context.Principal;
        string? tenantId = principal?.FindFirst("tid")?.Value;
        string? objectId = principal?.FindFirst("oid")?.Value;

        if (!string.Equals(tenantId, entra.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            context.Fail("The token was not issued for the allowed tenant.");
        }
        else if (!Guid.TryParse(objectId, out _))
        {
            context.Fail("The token has no valid 'oid' claim.");
        }
        else if (principal!.HasClaim(claim => claim.Type == "nonce"))
        {
            context.Fail("ID tokens are not accepted; send an access token for this API.");
        }
    }
}
