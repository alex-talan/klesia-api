namespace Domain.Users;

/// <summary>
/// An entry of the authorization allowlist (<c>sec.User</c>).
/// <see cref="UserId"/> is the internal SQL identity; <see cref="UserUniqueId"/> is the Microsoft Entra <c>oid</c>.
/// </summary>
public sealed record ApplicationUser(int UserId, Guid UserUniqueId, string Name, bool IsActive);
