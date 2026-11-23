using Klesia.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Klesia.Infrastructure.Notifications;

internal sealed class AzureSignalRNotifier : INotifier
{
    private static readonly TimeSpan NotificationTimeout = TimeSpan.FromSeconds(5);
    private readonly IDemandsHubClient _hubClient;
    private readonly ILogger<AzureSignalRNotifier> _logger;
    private readonly TimeSpan _notificationTimeout;

    public AzureSignalRNotifier(
        IDemandsHubClient hubClient,
        ILogger<AzureSignalRNotifier> logger)
        : this(hubClient, logger, NotificationTimeout)
    {
    }

    internal AzureSignalRNotifier(
        IDemandsHubClient hubClient,
        ILogger<AzureSignalRNotifier> logger,
        TimeSpan notificationTimeout)
    {
        ArgumentNullException.ThrowIfNull(hubClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(notificationTimeout, TimeSpan.Zero);

        _hubClient = hubClient;
        _logger = logger;
        _notificationTimeout = notificationTimeout;
    }

    public async Task NotifyDemandsChangedAsync(Guid ownerOid, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_notificationTimeout);

        try
        {
            await _hubClient.SendToUserAsync(
                ownerOid.ToString(),
                "demandUpdated",
                Array.Empty<object>(),
                timeout.Token);
        }
        catch (Exception)
        {
            _logger.LogWarning("notification failed");
        }
    }
}