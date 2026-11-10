// Application service template for Klesia (net10.0).
// Business failures are exceptions; WebApi maps them to ProblemDetails (GlobalExceptionHandler).
// No Result<T>, FluentValidation, cache or Redis: they are not part of the stack.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Klesia.Templates;

#region Domain/Users and Domain/Demands

public sealed record User(int UserId, Guid UserUniqueId, string Name, string Email);

/// <summary>
/// Value object guarding its own invariant: format GK######-######### (mirrors CK_Demand_GKNumber).
/// </summary>
public readonly partial record struct GKNumber
{
    private GKNumber(string value) => Value = value;

    public string Value { get; }

    [GeneratedRegex(@"^GK[0-9]{6}-[0-9]{9}\z", RegexOptions.CultureInvariant)]
    private static partial Regex GkPattern();

    public static GKNumber Parse(string? value)
    {
        if (value is null || !GkPattern().IsMatch(value))
        {
            throw new ArgumentException("A GK number must match GK######-######### (e.g. GK260902-144743112).", nameof(value));
        }

        return new GKNumber(value);
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

public sealed record DemandDto(int DemandId, string GKNumber, string BaeLink, string Status);

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
            items.Select(d => new DemandDto(d.DemandId, d.GKNumber, d.BaeLink, d.StatusCode)).ToList());
    }

    public async Task<int> CreateAsync(
        Guid userUniqueId,
        string gkNumber,
        string baeLink,
        CancellationToken cancellationToken)
    {
        User user = await users.FindActiveByUniqueIdAsync(userUniqueId, cancellationToken)
            ?? throw new ForbiddenException("The caller is not an active user.");

        GKNumber gk = GKNumber.Parse(gkNumber);

        if (!Uri.TryCreate(baeLink, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The BAE link must be an absolute https URL.", nameof(baeLink));
        }

        return await demands.CreateAsync(user.UserId, gk.Value, uri.AbsoluteUri, cancellationToken);
    }
}

#endregion
