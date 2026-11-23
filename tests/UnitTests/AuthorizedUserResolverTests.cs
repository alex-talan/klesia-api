using FluentAssertions;
using Klesia.Application.Authorization;
using Klesia.Application.Abstractions;
using Klesia.Domain.Users;
using NSubstitute;

namespace UnitTests;

public sealed class AuthorizedUserResolverTests
{
    [Fact]
    public async Task UnknownUserThrowsAccessDenied()
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetByUniqueIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ApplicationUser?)null);
        var resolver = new AuthorizedUserResolver(repository);

        var act = () => resolver.ResolveAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task InactiveUserThrowsAccessDenied()
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetByUniqueIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(
            new ApplicationUser(7, Guid.NewGuid(), "Inactive", false));
        var resolver = new AuthorizedUserResolver(repository);

        var act = () => resolver.ResolveAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task ActiveUserIsResolvedWithInternalId()
    {
        var oid = Guid.NewGuid();
        var repository = Substitute.For<IUserRepository>();
        repository.GetByUniqueIdAsync(oid, Arg.Any<CancellationToken>()).Returns(
            new ApplicationUser(42, oid, "Active", true));
        var resolver = new AuthorizedUserResolver(repository);

        var user = await resolver.ResolveAsync(oid, CancellationToken.None);

        user.Id.Should().Be(42);
        user.UserUniqueId.Should().Be(oid);
    }

    [Fact]
    public async Task RepositoryFailurePropagates()
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetByUniqueIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<ApplicationUser?>>(_ => throw new InvalidOperationException("Database unavailable"));
        var resolver = new AuthorizedUserResolver(repository);

        var act = () => resolver.ResolveAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database unavailable");
    }
}
