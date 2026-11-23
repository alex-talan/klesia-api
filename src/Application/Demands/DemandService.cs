using Klesia.Application.Abstractions;
using Klesia.Application.Authorization;

namespace Klesia.Application.Demands;

public sealed class DemandService
{
    private readonly AuthorizedUserResolver _authorizedUserResolver;
    private readonly IDemandRepository _demandRepository;

    public DemandService(
        AuthorizedUserResolver authorizedUserResolver,
        IDemandRepository demandRepository)
    {
        _authorizedUserResolver = authorizedUserResolver;
        _demandRepository = demandRepository;
    }

    public async Task<UserDemandsResult> ListAsync(Guid oid, CancellationToken cancellationToken)
    {
        var user = await _authorizedUserResolver.ResolveAsync(oid, cancellationToken);
        var demands = await _demandRepository.ListByUserAsync(user.Id, cancellationToken);

        var items = demands
            .Select(demand => new DemandListItem(
                demand.Id,
                demand.GKNumber.Value,
                demand.Status.Code))
            .ToArray();

        return new UserDemandsResult(user.UserUniqueId, user.Name, items);
    }
}