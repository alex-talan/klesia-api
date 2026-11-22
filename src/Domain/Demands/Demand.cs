namespace Klesia.Domain.Demands;

public sealed record Demand(
    int Id,
    int UserId,
    GKNumber GKNumber,
    DemandStatus Status);