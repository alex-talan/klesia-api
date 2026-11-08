using Application.Abstractions;
using Application.Common.Exceptions;
using Application.Demands;
using Domain.Demands;
using Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace UnitTests.Application;

public sealed class DemandServiceTests
{
    private static readonly Guid UserUniqueId = Guid.Parse("f47ac10b-58cc-4372-a567-0e02b2c3d479");

    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly IDemandRepository demands = Substitute.For<IDemandRepository>();
    private readonly IBaeLinkResolver links = Substitute.For<IBaeLinkResolver>();

    public DemandServiceTests()
    {
        links.Resolve(Arg.Any<string>()).Returns(call => call.Arg<string>());
    }

    [Fact]
    public async Task GetUserDemandsAsync_ActiveUser_ReturnsOwnDemandsInRepositoryOrder()
    {
        users.FindByUniqueIdAsync(UserUniqueId, Arg.Any<CancellationToken>())
            .Returns(new ApplicationUser(7, UserUniqueId, "John DOE", true));
        IReadOnlyList<Demand> owned =
        [
            new Demand(2, SirenNumber.Create("732829320"), "https://files.example.com/bae-2.pdf", "ok"),
            new Demand(1, SirenNumber.Create("732829320"), "https://files.example.com/bae-1.pdf", "pending")
        ];
        demands.ListByUserIdAsync(7, Arg.Any<CancellationToken>()).Returns(owned);

        UserDemandsDto result = await CreateSut().GetUserDemandsAsync(UserUniqueId, CancellationToken.None);

        result.UserId.Should().Be(UserUniqueId);
        result.UserName.Should().Be("John DOE");
        result.Demands.Should().Equal(
            new DemandDto(2, "732-829-320", "https://files.example.com/bae-2.pdf", "ok"),
            new DemandDto(1, "732-829-320", "https://files.example.com/bae-1.pdf", "pending"));
        await demands.DidNotReceive().ListByUserIdAsync(Arg.Is<int>(id => id != 7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserDemandsAsync_ActiveUserWithoutDemands_ReturnsEmptyList()
    {
        users.FindByUniqueIdAsync(UserUniqueId, Arg.Any<CancellationToken>())
            .Returns(new ApplicationUser(7, UserUniqueId, "John DOE", true));
        IReadOnlyList<Demand> none = [];
        demands.ListByUserIdAsync(7, Arg.Any<CancellationToken>()).Returns(none);

        UserDemandsDto result = await CreateSut().GetUserDemandsAsync(UserUniqueId, CancellationToken.None);

        result.Demands.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserDemandsAsync_UnknownUser_ThrowsForbiddenWithoutReadingDemands()
    {
        users.FindByUniqueIdAsync(UserUniqueId, Arg.Any<CancellationToken>())
            .Returns((ApplicationUser?)null);

        Func<Task> act = () => CreateSut().GetUserDemandsAsync(UserUniqueId, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await demands.DidNotReceiveWithAnyArgs().ListByUserIdAsync(default, default);
    }

    [Fact]
    public async Task GetUserDemandsAsync_InactiveUser_ThrowsForbiddenWithSameMessageAsUnknownUser()
    {
        users.FindByUniqueIdAsync(UserUniqueId, Arg.Any<CancellationToken>())
            .Returns(new ApplicationUser(7, UserUniqueId, "John DOE", false));

        Func<Task> act = () => CreateSut().GetUserDemandsAsync(UserUniqueId, CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Message.Should().Be(new ForbiddenException().Message);
        await demands.DidNotReceiveWithAnyArgs().ListByUserIdAsync(default, default);
    }

    [Fact]
    public async Task GetUserDemandsAsync_UsesTheResolvedLink()
    {
        users.FindByUniqueIdAsync(UserUniqueId, Arg.Any<CancellationToken>())
            .Returns(new ApplicationUser(7, UserUniqueId, "John DOE", true));
        IReadOnlyList<Demand> owned = [new Demand(1, SirenNumber.Create("732829320"), "https://stored.example.com/a.pdf", "fail")];
        demands.ListByUserIdAsync(7, Arg.Any<CancellationToken>()).Returns(owned);
        links.Resolve("https://stored.example.com/a.pdf").Returns("https://resolved.example.com/a.pdf");

        UserDemandsDto result = await CreateSut().GetUserDemandsAsync(UserUniqueId, CancellationToken.None);

        result.Demands.Single().BaeLink.Should().Be("https://resolved.example.com/a.pdf");
    }

    private DemandService CreateSut() => new(users, demands, links);
}
