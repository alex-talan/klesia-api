using System.Diagnostics.CodeAnalysis;

namespace Klesia.Domain.Demands;

public sealed record DemandStatus
{
    private DemandStatus(string code)
    {
        Code = code;
    }

    public static DemandStatus Pending { get; } = new("pending");

    public static DemandStatus Ok { get; } = new("ok");

    public static DemandStatus Fail { get; } = new("fail");

    public string Code { get; }

    public static DemandStatus FromCode(string? code)
    {
        if (TryFromCode(code, out var status))
        {
            return status;
        }

        throw new ArgumentException("Demand status code is not recognized.", nameof(code));
    }

    public static bool TryFromCode(string? code, [NotNullWhen(true)] out DemandStatus? status)
    {
        status = code switch
        {
            "pending" => Pending,
            "ok" => Ok,
            "fail" => Fail,
            _ => null
        };

        return status is not null;
    }
}