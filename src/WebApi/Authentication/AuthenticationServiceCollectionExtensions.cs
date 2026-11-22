using System.Security.Claims;
using Klesia.Infrastructure.Options;
using Klesia.WebApi.Errors;
using Klesia.WebApi.Logging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Klesia.WebApi.Authentication;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddKlesiaAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var azureAd = configuration.GetSection(AzureAdOptions.SectionName).Get<AzureAdOptions>()
            ?? new AzureAdOptions();

        var instance = azureAd.Instance.TrimEnd('/');
        var authority = $"{instance}/{azureAd.TenantId}/v2.0";
        var audiences = azureAd.Audiences is { Length: > 0 }
            ? azureAd.Audiences
            : [azureAd.ClientId, $"api://{azureAd.ClientId}"];

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = authority,
                    ValidAudiences = audiences,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(2)
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        var claims = context.Principal?.Claims;
                        var tenantId = claims?.FirstOrDefault(claim => claim.Type == "tid")?.Value;
                        var oid = claims?.FirstOrDefault(claim => claim.Type == "oid")?.Value;

                        if (!string.Equals(tenantId, azureAd.TenantId, StringComparison.OrdinalIgnoreCase)
                            || claims?.Any(claim => claim.Type == "nonce") == true
                            || !Guid.TryParseExact(oid, "D", out _))
                        {
                            context.Fail("The access token is invalid.");
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";
                        context.Response.Headers.WWWAuthenticate = "Bearer";

                        var traceId = context.HttpContext.Items.TryGetValue(
                            CorrelationIdMiddleware.ItemName,
                            out var value)
                            ? value?.ToString()
                            : context.HttpContext.TraceIdentifier;

                        await Results.Json(new
                        {
                            title = "Unauthorized",
                            status = StatusCodes.Status401Unauthorized,
                            traceId
                        }, contentType: "application/problem+json", statusCode: StatusCodes.Status401Unauthorized)
                            .ExecuteAsync(context.HttpContext);
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }
}
