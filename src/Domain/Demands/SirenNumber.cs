using System.Diagnostics.CodeAnalysis;

namespace Domain.Demands;

/// <summary>
/// French company identifier: exactly nine ASCII digits, kept as text to preserve leading zeros.
/// </summary>
public sealed record SirenNumber
{
    public const int Length = 9;

    private SirenNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SirenNumber Create(string? value)
    {
        return TryCreate(value, out SirenNumber? siren)
            ? siren
            : throw new ArgumentException("A SIREN number must contain exactly nine digits.", nameof(value));
    }

    public static bool TryCreate(string? value, out [NotNullWhen(true)] SirenNumber? siren)
    {
        if (value is { Length: Length } && value.All(character => character is >= '0' and <= '9'))
        {
            siren = new SirenNumber(value);
            return true;
        }

        siren = null;
        return false;
    }

    /// <summary>Presents the value as <c>XXX-XXX-XXX</c>.</summary>
    public string ToFormattedString() => $"{Value[..3]}-{Value[3..6]}-{Value[6..]}";

    public override string ToString() => Value;
}
