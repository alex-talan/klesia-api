using System.Security.Claims;

namespace Klesia.WebApi.Authentication;

public sealed class CurrentPrincipal : ICurrentPrincipal
{
    public Guid GetOid(ClaimsPrincipal principal)
    {
        var oid = principal.FindFirst("oid")?.Value;
        if (!Guid.TryParseExact(oid, "D", out var parsedOid))
        {
            throw new InvalidOperationException("The validated principal does not contain a valid oid claim.");
        }

        return parsedOid;
    }
}
