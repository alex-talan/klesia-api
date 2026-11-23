using Klesia.Domain.Demands;

namespace Klesia.Application.Abstractions;

public interface IDemandRepository
{
    public Task<IReadOnlyList<Demand>> ListByUserAsync(int userId, CancellationToken cancellationToken);

    public Task<string?> GetOwnedBaeLinkAsync(int demandId, int userId, CancellationToken cancellationToken);
}