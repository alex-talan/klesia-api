using Application.Abstractions;
using Application.Common.Exceptions;
using Domain.Demands;
using Domain.Users;

namespace Application.Demands;

public sealed class DemandService(
    IUserRepository userRepository,
    IDemandRepository demandRepository,
    IBaeLinkResolver baeLinkResolver)
{
    /// <summary>
    /// Returns the demands owned by the caller identified by the validated Entra <c>oid</c>.
    /// The caller must exist in the allowlist and be active; otherwise <see cref="ForbiddenException"/> is thrown.
    /// </summary>
    public async Task<UserDemandsDto> GetUserDemandsAsync(Guid userUniqueId, CancellationToken cancellationToken)
    {
        ApplicationUser? user = await userRepository.FindByUniqueIdAsync(userUniqueId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new ForbiddenException();
        }

        IReadOnlyList<Demand> demands = await demandRepository.ListByUserIdAsync(user.UserId, cancellationToken);

        DemandDto[] items = demands
            .Select(demand => new DemandDto(
                demand.DemandId,
                demand.SirenNumber.ToFormattedString(),
                baeLinkResolver.Resolve(demand.BaeLink),
                demand.StatusCode))
            .ToArray();

        return new UserDemandsDto(user.UserUniqueId, user.Name, items);
    }
}
