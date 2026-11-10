// Application service template for Klesia (net10.0).
// Business failures are exceptions; WebApi maps them to ProblemDetails (GlobalExceptionHandler).
// No Result<T>, FluentValidation, cache or Redis: they are not part of the stack.

using Microsoft.Extensions.Logging;

namespace Klesia.Templates;

#region Domain/Users and Domain/Demands

public sealed record User(int UserId, Guid UserUniqueId, string Name, string Email);

/// <summary>
/// Value object guarding its own invariant: exactly nine ASCII digits (mirrors CK_Demand_SirenNumber).
/// </summary>
public readonly record struct SirenNumber
{
    private SirenNumber(string value) => Value = value;

    public string Value { get; }

    public static SirenNumber Parse(string? value)
    {
        if (value is null || value.Length != 9 || !value.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("A SIREN number must be exactly nine digits.", nameof(value));
        }

        return new SirenNumber(value);
    }

    public override string ToString() => Value;
}

#endregion

#region Application/Common/Exceptions

public sealed class NotFoundException(string message) : Exception(message);

public sealed class ForbiddenException(string message) : Exception(message);

#endregion

#region Application/Abstractions

public interface IUserRepository
{
    Task<User?> FindActiveByUniqueIdAsync(Guid userUniqueId, CancellationToken cancellationToken);
}

#endregion

#region Application/Demands

public sealed record DemandDto(int DemandId, string SirenNumber, string BaeLink, string Status);

public sealed record UserDemandsDto(string UserName, IReadOnlyList<DemandDto> Demands);

public sealed class DemandService(
    IUserRepository users,
    IDemandRepository demands,
    ILogger<DemandService> logger)
{
    public async Task<UserDemandsDto> GetForUserAsync(Guid userUniqueId, CancellationToken cancellationToken)
    {
        User user = await users.FindActiveByUniqueIdAsync(userUniqueId, cancellationToken)
            ?? throw new ForbiddenException("The caller is not an active user.");

        IReadOnlyList<Demand> items = await demands.ListByUserAsync(user.UserId, cancellationToken);

        logger.LogInformation("Returned {DemandCount} demands for user {UserId}", items.Count, user.UserId);

        return new UserDemandsDto(
            user.Name,
            items.Select(d => new DemandDto(d.DemandId, d.SirenNumber, d.BaeLink, d.StatusCode)).ToList());
    }

    public async Task<int> CreateAsync(
        Guid userUniqueId,
        string sirenNumber,
        string baeLink,
        CancellationToken cancellationToken)
    {
        User user = await users.FindActiveByUniqueIdAsync(userUniqueId, cancellationToken)
            ?? throw new ForbiddenException("The caller is not an active user.");

        SirenNumber siren = SirenNumber.Parse(sirenNumber);

        if (!Uri.TryCreate(baeLink, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The BAE link must be an absolute https URL.", nameof(baeLink));
        }

        return await demands.CreateAsync(user.UserId, siren.Value, uri.AbsoluteUri, cancellationToken);
    }
}

#endregion
