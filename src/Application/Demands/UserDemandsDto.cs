namespace Application.Demands;

/// <param name="UserId">The Microsoft Entra <c>oid</c> (<c>sec.User.UserUniqueId</c>), never the SQL identity.</param>
public sealed record UserDemandsDto(Guid UserId, string UserName, IReadOnlyList<DemandDto> Demands);
