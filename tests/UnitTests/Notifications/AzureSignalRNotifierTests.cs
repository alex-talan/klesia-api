using FluentAssertions;
using Klesia.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;

namespace UnitTests.Notifications;

public sealed class AzureSignalRNotifierTests
{
    private static readonly Guid OwnerOid = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public async Task NotifyDemandsChangedAsync_SendsEmptyEventToOwnerOnly()
    {
        var hubClient = Substitute.For<IDemandsHubClient>();
        var logger = Substitute.For<ILogger<AzureSignalRNotifier>>();
        var notifier = new AzureSignalRNotifier(hubClient, logger, TimeSpan.FromSeconds(1));

        await notifier.NotifyDemandsChangedAsync(OwnerOid, CancellationToken.None);

        await hubClient.Received(1).SendToUserAsync(
            OwnerOid.ToString(),
            "demandUpdated",
            Arg.Is<object?[]>(arguments => arguments.Length == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyDemandsChangedAsync_TransportFailureIsSwallowedAndRedacted()
    {
        var hubClient = Substitute.For<IDemandsHubClient>();
        var logger = Substitute.For<ILogger<AzureSignalRNotifier>>();
        hubClient.SendToUserAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("transport failure detail"));
        var notifier = new AzureSignalRNotifier(hubClient, logger, TimeSpan.FromSeconds(1));

        var act = () => notifier.NotifyDemandsChangedAsync(OwnerOid, CancellationToken.None);

        await act.Should().NotThrowAsync();
        AssertRedactedWarning(logger, OwnerOid.ToString());
    }

    [Fact]
    public async Task NotifyDemandsChangedAsync_TimeoutIsSwallowed()
    {
        var hubClient = Substitute.For<IDemandsHubClient>();
        var logger = Substitute.For<ILogger<AzureSignalRNotifier>>();
        hubClient.SendToUserAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>()));
        var notifier = new AzureSignalRNotifier(hubClient, logger, TimeSpan.FromMilliseconds(50));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await notifier.NotifyDemandsChangedAsync(OwnerOid, CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        AssertRedactedWarning(logger, OwnerOid.ToString());
    }

    private static void AssertRedactedWarning(ILogger<AzureSignalRNotifier> logger, string ownerOid)
    {
        var warning = logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => call.GetArguments())
            .Where(arguments => arguments[0] is LogLevel.Warning)
            .Should()
            .ContainSingle()
            .Which;

        var message = warning[2]?.ToString();
        message.Should().Be("notification failed");
        message.Should().NotContain(ownerOid);
        message.Should().NotContain("transport failure detail");
        warning[3].Should().BeNull();
    }
}