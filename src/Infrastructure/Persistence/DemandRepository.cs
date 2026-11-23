using Dapper;
using Klesia.Application.Abstractions;
using Klesia.Domain.Demands;
using Microsoft.Data.SqlClient;

namespace Klesia.Infrastructure.Persistence;

public sealed class DemandRepository : IDemandRepository
{
    private const string ListByUserSql = """
        SELECT d.Id AS DemandId,
               d.GKNumber,
               s.Code AS StatusCode
        FROM dbo.Demand AS d
        INNER JOIN ref.Status AS s ON s.Id = d.StatusId
        WHERE d.UserId = @UserId
        ORDER BY d.Id DESC;
        """;

        private const string GetOwnedBaeLinkSql = """
                SELECT d.BaeLink
                FROM dbo.Demand AS d
                WHERE d.Id = @DemandId
                    AND d.UserId = @UserId;
                """;

    private readonly ISqlConnectionFactory _connectionFactory;

    public DemandRepository(ISqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Demand>> ListByUserAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        await using var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        IEnumerable<DemandRow> rows = await connection.QueryAsync<DemandRow>(
            new CommandDefinition(
                ListByUserSql,
                new { UserId = userId },
                cancellationToken: cancellationToken));

        return rows
            .Select(row => new Demand(
                row.DemandId,
                userId,
                GKNumber.Create(row.GKNumber),
                DemandStatus.FromCode(row.StatusCode)))
            .ToArray();
    }

    public async Task<string?> GetOwnedBaeLinkAsync(
        int demandId,
        int userId,
        CancellationToken cancellationToken)
    {
        await using var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition(
                GetOwnedBaeLinkSql,
                new { DemandId = demandId, UserId = userId },
                cancellationToken: cancellationToken));
    }

    private sealed record DemandRow(int DemandId, string GKNumber, string StatusCode);
}