namespace Klesia.Domain.Users;

public sealed record ApplicationUser(
    int Id,
    Guid UserUniqueId,
    string Name,
    bool IsActive);