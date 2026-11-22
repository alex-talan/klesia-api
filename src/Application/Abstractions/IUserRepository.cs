using Klesia.Domain.Users;

namespace Klesia.Application.Abstractions;

public interface IUserRepository
{
    public Task<ApplicationUser?> GetByUniqueIdAsync(Guid oid, CancellationToken cancellationToken);
}
