using Domain.Users;

namespace Application.Abstractions;

public interface IUserRepository
{
    /// <summary>Finds an allowlist entry by Microsoft Entra object id, whatever its active state.</summary>
    Task<ApplicationUser?> FindByUniqueIdAsync(Guid userUniqueId, CancellationToken cancellationToken);
}
