using Application.Abstractions;
using Dapper;
using Domain.Users;
using Microsoft.Data.SqlClient;

namespace Infrastructure.Persistence.Repositories;

public sealed class UserRepository(ISqlConnectionFactory connectionFactory) : IUserRepository
{
    private const string FindByUniqueIdSql = """
        SELECT [UserId], [UserUniqueId], [Name], [IsActive]
        FROM [sec].[User]
        WHERE [UserUniqueId] = @UserUniqueId;
        """;

    public async Task<ApplicationUser?> FindByUniqueIdAsync(Guid userUniqueId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<ApplicationUser>(
            new CommandDefinition(
                FindByUniqueIdSql,
                new { UserUniqueId = userUniqueId },
                cancellationToken: cancellationToken));
    }
}
