using System.Diagnostics.CodeAnalysis;

namespace Klesia.Domain.Demands;

public sealed record GKNumber
{
    private GKNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static GKNumber Create(string? value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException("GK number must match GK######-#########.", nameof(value));
        }

        return new GKNumber(value!);
    }

    public static bool TryCreate(string? value, [NotNullWhen(true)] out GKNumber? number)
    {
        if (!IsValid(value))
        {
            number = null;
            return false;
        }

        number = new GKNumber(value!);
        return true;
    }

    public override string ToString() => Value;

    private static bool IsValid(string? value)
    {
        if (value is null || value.Length != 18 || value[0] != 'G' || value[1] != 'K' || value[8] != '-')
        {
            return false;
        }

        for (var index = 2; index < value.Length; index++)
        {
            if (index == 8)
            {
                continue;
            }

            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}