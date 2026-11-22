using Klesia.WebApi.Errors;
using Klesia.WebApi.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Klesia.WebApi.Authentication;

public sealed class KlesiaAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            var traceId = context.Items.TryGetValue(CorrelationIdMiddleware.ItemName, out var value)
                ? value?.ToString()
                : context.TraceIdentifier;

            await Problems.WriteForbiddenAsync(context, traceId);
            return;
        }

        if (authorizeResult.Challenged)
        {
            await context.ChallengeAsync();
            return;
        }

        await next(context);
    }
}
