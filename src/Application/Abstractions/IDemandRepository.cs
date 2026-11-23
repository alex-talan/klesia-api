using Klesia.Domain.Demands;

namespace Klesia.Application.Abstractions;

public interface IDemandRepository
{
    public Task<IReadOnlyList<Demand>> ListByUserAsync(int userId, CancellationToken cancellationToken);
}