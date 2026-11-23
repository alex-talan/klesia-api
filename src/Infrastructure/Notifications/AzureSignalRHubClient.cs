using Klesia.Infrastructure.Options;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Klesia.Infrastructure.Notifications;

internal sealed class AzureSignalRHubClient : IDemandsHubClient, IAsyncDisposable
{
    private readonly ServiceManager _serviceManager;
    private readonly string _hubName;
    private readonly ILogger<AzureSignalRNotifier> _logger;
    private readonly Lazy<Task<ServiceHubContext>> _hubContext;

    public AzureSignalRHubClient(
        ServiceManager serviceManager,
        ILogger<AzureSignalRNotifier> logger,
        IOptions<AzureSignalROptions> options)
    {
        ArgumentNullException.ThrowIfNull(serviceManager);
        ArgumentNullException.ThrowIfNull(options);

        _serviceManager = serviceManager;
        _hubName = options.Value.HubName;
        _logger = logger;
        _hubContext = new Lazy<Task<ServiceHubContext>>(
            () => _serviceManager.CreateHubContextAsync(_hubName, CancellationToken.None));
    }

    public async Task SendToUserAsync(
        string userId,
        string methodName,
        object?[] arguments,
        CancellationToken cancellationToken)
    {
        var hubContext = await GetHubContextAsync(cancellationToken);
        await hubContext.Clients.User(userId).SendCoreAsync(methodName, arguments, cancellationToken);
    }

    public async Task<SignalRNegotiation> NegotiateAsync(string userId, CancellationToken cancellationToken)
    {
        var hubContext = await GetHubContextAsync(cancellationToken);
        var response = await hubContext.NegotiateAsync(
            new NegotiationOptions { UserId = userId },
            cancellationToken);
        return new SignalRNegotiation(response.Url, response.AccessToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_hubContext.IsValueCreated)
        {
            return;
        }

        var contextTask = _hubContext.Value;
        try
        {
            var hubContext = await contextTask;
            await hubContext.DisposeAsync();
        }
        catch when (contextTask.IsFaulted || contextTask.IsCanceled)
        {
            _logger.LogWarning("notification failed");
        }
    }

    private Task<ServiceHubContext> GetHubContextAsync(CancellationToken cancellationToken) =>
        _hubContext.Value.WaitAsync(cancellationToken);
}