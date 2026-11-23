using System.Security.Claims;

namespace Klesia.WebApi.Authentication;

public interface ICurrentPrincipal
{
    public Guid GetOid(ClaimsPrincipal principal);
}
