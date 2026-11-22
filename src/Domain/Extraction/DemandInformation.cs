namespace Klesia.Domain.Extraction;

public sealed record DemandInformation
{
    public int Id { get; init; }

    public int DemandId { get; init; }

    public string Brand { get; init; } = string.Empty;

    public string Branch { get; init; } = string.Empty;

    public string RiskType { get; init; } = string.Empty;

    public string Siren { get; init; } = string.Empty;

    public int? ReferrerCode { get; init; }

    public string? ReferrerNetwork { get; init; }

    public string Social { get; init; } = string.Empty;

    public string LegalStatus { get; init; } = string.Empty;

    public string? HeadquarterNic { get; init; }

    public string? HeadquarterAddress { get; init; }

    public string? HeadquarterZip { get; init; }

    public string? HeadquarterCity { get; init; }

    public string? HeadquarterPhone { get; init; }

    public string? Email { get; init; }

    public string NafCode { get; init; } = string.Empty;

    public int Idcc { get; init; }

    public string? ReferrerIdentity { get; init; }

    public string? ReferrerEmail { get; init; }

    public string? EffectDateDesired { get; init; }

    public string CoveredCategory { get; init; } = string.Empty;

    public int CoveredAmount { get; init; }

    public string EstablishmentSiret { get; init; } = string.Empty;

    public string? EstablishmentDesignation { get; init; }

    public string? EstablishmentZip { get; init; }

    public string Contribution { get; init; } = string.Empty;

    public IReadOnlyList<DemandFormula> Formulas { get; init; } = Array.Empty<DemandFormula>();
}