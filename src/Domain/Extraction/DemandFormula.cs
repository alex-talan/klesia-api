namespace Klesia.Domain.Extraction;

public sealed record DemandFormula(
    int Id,
    int DemandInformationId,
    string Name,
    string? Detail);