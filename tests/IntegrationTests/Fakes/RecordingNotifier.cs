using System.Collections.Concurrent;
using Klesia.Application.Abstractions;

namespace IntegrationTests.Fakes;

public sealed class RecordingNotifier : INotifier
{
    private readonly ConcurrentQueue<Guid> _ownerOids = new();

    public IReadOnlyCollection<Guid> OwnerOids => _ownerOids.ToArray();

    public bool ThrowOnNotify { get; set; }

    public TimeSpan Delay { get; set; }

    public async Task NotifyDemandsChangedAsync(Guid ownerOid, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        if (ThrowOnNotify)
        {
            throw new InvalidOperationException("RecordingNotifier configured to fail.");
        }

        _ownerOids.Enqueue(ownerOid);
    }
}