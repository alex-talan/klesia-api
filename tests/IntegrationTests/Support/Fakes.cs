using Application.Abstractions;
using Domain.Demands;
using Domain.Users;

namespace IntegrationTests.Support;

public sealed class FakeUserRepository : IUserRepository
{
    private readonly List<ApplicationUser> users = [];

    /// <summary>When set, every call throws it (simulates an unavailable database).</summary>
    public Exception? Failure { get; set; }

    public void Add(ApplicationUser user) => users.Add(user);

    public Task<ApplicationUser?> FindByUniqueIdAsync(Guid userUniqueId, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        return Task.FromResult<ApplicationUser?>(users.SingleOrDefault(user => user.UserUniqueId == userUniqueId));
    }
}

public sealed class FakeDemandRepository : IDemandRepository
{
    private readonly List<(int UserId, Demand Demand)> demands = [];

    public void Add(int userId, Demand demand) => demands.Add((userId, demand));

    public Task<IReadOnlyList<Demand>> ListByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<Demand> result = demands
            .Where(entry => entry.UserId == userId)
            .Select(entry => entry.Demand)
            .OrderByDescending(demand => demand.DemandId)
            .ToArray();

        return Task.FromResult(result);
    }
}
