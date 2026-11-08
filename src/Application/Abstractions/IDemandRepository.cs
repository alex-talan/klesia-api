using Domain.Demands;

namespace Application.Abstractions;

public interface IDemandRepository
{
    /// <summary>Lists every demand owned by the user, most recent first (<c>DemandId</c> descending).</summary>
    Task<IReadOnlyList<Demand>> ListByUserIdAsync(int userId, CancellationToken cancellationToken);
}
