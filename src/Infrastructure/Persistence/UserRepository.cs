using System.Data;
using Dapper;
using Klesia.Application.Abstractions;
using Klesia.Domain.Users;

namespace Klesia.Infrastructure.Persistence;

public sealed class UserRepository : IUserRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;

    public UserRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ApplicationUser?> GetByUniqueIdAsync(
        Guid oid,
        CancellationToken cancellationToken)
    {
        await using var connection = (Microsoft.Data.SqlClient.SqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var user = await connection.QuerySingleOrDefaultAsync<UserRecord>(
            new CommandDefinition(
                "SELECT u.Id AS Id, u.UserUniqueId AS UserUniqueId, u.Name AS Name, u.IsActive AS IsActive "
                + "FROM sec.[User] AS u WHERE u.UserUniqueId = @Oid;",
                new { Oid = oid },
                commandType: CommandType.Text,
                cancellationToken: cancellationToken));

        return user is null
            ? null
            : new ApplicationUser(user.Id, user.UserUniqueId, user.Name, user.IsActive);
    }

    private sealed record UserRecord(
        int Id,
        Guid UserUniqueId,
        string Name,
        bool IsActive);
}
