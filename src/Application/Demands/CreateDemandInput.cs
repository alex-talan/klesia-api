using Klesia.Domain.Demands;

namespace Klesia.Application.Demands;

public sealed record CreateDemandInput(
    string GkNumber,
    string? DeclaredContentType,
    long Length,
    Stream Content);

public abstract record CreateDemandResult
{
    private CreateDemandResult()
    {
    }

    public sealed record Created(Demand Demand) : CreateDemandResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : CreateDemandResult;

    public sealed record TooLarge : CreateDemandResult;

    public sealed record Unprocessable(IReadOnlyDictionary<string, string[]> Errors) : CreateDemandResult;

    public sealed record StorageUnavailable : CreateDemandResult;
}