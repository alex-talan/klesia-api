namespace Klesia.Application.Abstractions;

public interface INotifier
{
    /// <summary>Best-effort notification that the owner's demands changed; implementations must not throw.</summary>
    public Task NotifyDemandsChangedAsync(Guid ownerOid, CancellationToken cancellationToken);
}