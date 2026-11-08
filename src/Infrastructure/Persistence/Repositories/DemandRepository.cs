using Application.Abstractions;
using Dapper;
using Domain.Demands;
using Microsoft.Data.SqlClient;

namespace Infrastructure.Persistence.Repositories;

public sealed class DemandRepository(ISqlConnectionFactory connectionFactory) : IDemandRepository
{
    private const string ListByUserIdSql = """
        SELECT d.[DemandId], d.[SirenNumber], d.[BaeLink], s.[Code] AS [StatusCode]
        FROM [dbo].[Demand] AS d
        INNER JOIN [dbo].[Status] AS s ON s.[StatusId] = d.[StatusId]
        WHERE d.[UserId] = @UserId
        ORDER BY d.[DemandId] DESC;
        """;

    public async Task<IReadOnlyList<Demand>> ListByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = connectionFactory.Create();
        await connection.OpenAsync(cancellationToken);

        IEnumerable<DemandRow> rows = await connection.QueryAsync<DemandRow>(
            new CommandDefinition(
                ListByUserIdSql,
                new { UserId = userId },
                cancellationToken: cancellationToken));

        return rows
            .Select(row => new Demand(row.DemandId, SirenNumber.Create(row.SirenNumber), row.BaeLink, row.StatusCode))
            .ToArray();
    }

    private sealed record DemandRow(int DemandId, string SirenNumber, string BaeLink, string StatusCode);
}
