namespace Klesia.Application.Demands;

public sealed record UserDemandsResult(
    Guid UserId,
    string UserName,
    IReadOnlyList<DemandListItem> Demands);