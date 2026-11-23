using Klesia.Application.Abstractions;
using Klesia.Domain.Users;

namespace Klesia.Application.Authorization;

public sealed class AuthorizedUserResolver
{
    private readonly IUserRepository _userRepository;

    public AuthorizedUserResolver(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<ApplicationUser> ResolveAsync(
        Guid oid,
        CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByUniqueIdAsync(oid, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new AccessDeniedException();
        }

        return user;
    }
}
