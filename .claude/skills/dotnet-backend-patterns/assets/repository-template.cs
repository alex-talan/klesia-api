// Repository template for Klesia (net10.0, Dapper over Microsoft.Data.SqlClient).
// Illustrative only: the real schema is owned by database/Klesia.Database.
// Each region below belongs in a different project; the paths are in the region names.

using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Klesia.Templates;

#region Domain/Demands/Demand.cs

public sealed record Demand(int DemandId, string GKNumber, string BaeLink, string StatusCode);

#endregion

#region Application/Abstractions/IDemandRepository.cs

public interface IDemandRepository
{
    Task<Demand?> FindByIdAsync(int demandId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Demand>> ListByUserAsync(int userId, CancellationToken cancellationToken);

    Task<int> CreateAsync(int userId, string gkNumber, string baeLink, CancellationToken cancellationToken);

    Task<bool> UpdateStatusAsync(int demandId, string statusCode, CancellationToken cancellationToken);
}

#endregion

#region Infrastructure/Persistence/ISqlConnectionFactory.cs

public interface ISqlConnectionFactory
{
    SqlConnection CreateConnection();
}

internal sealed class SqlConnectionFactory(IOptions<SqlServerOptions> options) : ISqlConnectionFactory
{
    public SqlConnection CreateConnection() => new(options.Value.ConnectionString);
}

public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public string ConnectionString { get; set; } = string.Empty;
}

#endregion

#region Infrastructure/Persistence/Repositories/DemandRepository.cs

internal sealed class DemandRepository(ISqlConnectionFactory connectionFactory) : IDemandRepository
{
    public async Task<Demand?> FindByIdAsync(int demandId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.Id AS DemandId, d.GKNumber, d.BaeLink, s.Code AS StatusCode
            FROM dbo.Demand AS d
            INNER JOIN ref.Status AS s ON s.Id = d.StatusId
            WHERE d.Id = @DemandId
            """;

        await using SqlConnection connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Demand>(
            new CommandDefinition(sql, new { DemandId = demandId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Demand>> ListByUserAsync(int userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.Id AS DemandId, d.GKNumber, d.BaeLink, s.Code AS StatusCode
            FROM dbo.Demand AS d
            INNER JOIN ref.Status AS s ON s.Id = d.StatusId
            WHERE d.UserId = @UserId
            ORDER BY d.Id DESC
            """;

        await using SqlConnection connection = connectionFactory.CreateConnection();
        IEnumerable<Demand> rows = await connection.QueryAsync<Demand>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: cancellationToken));

        return rows.AsList();
    }

    public async Task<int> CreateAsync(
        int userId,
        string gkNumber,
        string baeLink,
        CancellationToken cancellationToken)
    {
        // The status id is resolved in SQL from its stable code, never hardcoded in C#.
        const string sql = """
            INSERT INTO dbo.Demand (UserId, GKNumber, BaeLink, StatusId)
            OUTPUT INSERTED.Id
            SELECT @UserId, @GKNumber, @BaeLink, s.Id
            FROM ref.Status AS s
            WHERE s.Code = N'pending'
            """;

        await using SqlConnection connection = connectionFactory.CreateConnection();
        try
        {
            return await connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    sql,
                    new { UserId = userId, GKNumber = gkNumber, BaeLink = baeLink },
                    cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601 or 547 or 515)
        {
            // Constraint violation: translate it so Application never sees SqlException.
            throw new InvalidOperationException("The demand violates a database constraint.", ex);
        }
    }

    public async Task<bool> UpdateStatusAsync(int demandId, string statusCode, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE d
            SET d.StatusId = s.Id
            FROM dbo.Demand AS d
            INNER JOIN ref.Status AS s ON s.Code = @StatusCode
            WHERE d.Id = @DemandId
            """;

        await using SqlConnection connection = connectionFactory.CreateConnection();
        int affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { DemandId = demandId, StatusCode = statusCode },
                cancellationToken: cancellationToken));

        return affected > 0;
    }
}

#endregion

#region Multi-statement transaction (only when several statements must succeed together)

internal static class TransactionExample
{
    public static async Task ExecuteAsync(
        ISqlConnectionFactory connectionFactory,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE dbo.Demand SET StatusId = @StatusId WHERE Id = @DemandId",
                new { StatusId = 2, DemandId = 1 },
                transaction,
                cancellationToken: cancellationToken));

            // ... further statements sharing the same transaction ...

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}

#endregion
