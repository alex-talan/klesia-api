// Repository template for Klesia (net10.0, Dapper over MySqlConnector, Azure Database for MySQL 8.4).
// Illustrative only: the real schema is owned by the DbUp migrator in database/Klesia.Database.
// Tables (one database, no schemas in MySQL): sec_Role, sec_User, ref_Status, Demand.
// Each region below belongs in a different project; the paths are in the region names.

using Azure.Core;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;

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

#region Infrastructure/Persistence/IDbConnectionFactory.cs

public interface IDbConnectionFactory
{
    MySqlConnection CreateConnection();
}

internal sealed class MySqlConnectionFactory : IDbConnectionFactory, IAsyncDisposable
{
    private static readonly string[] EntraScopes = ["https://ossrdbms-aad.database.windows.net/.default"];

    private readonly MySqlDataSource dataSource;

    public MySqlConnectionFactory(IOptions<DatabaseOptions> options, TokenCredential credential)
    {
        MySqlDataSourceBuilder builder = new(options.Value.ConnectionString);

        // Passwordless (spec DEC-06): without a Password in the connection string, a Microsoft Entra
        // access token is the MySQL password. Tokens live 5 to 60 minutes, so refresh well inside that.
        if (string.IsNullOrEmpty(new MySqlConnectionStringBuilder(options.Value.ConnectionString).Password))
        {
            builder.UsePeriodicPasswordProvider(
                async (_, cancellationToken) =>
                {
                    AccessToken token = await credential.GetTokenAsync(new TokenRequestContext(EntraScopes), cancellationToken);
                    return token.Token;
                },
                successRefreshInterval: TimeSpan.FromMinutes(10),
                failureRefreshInterval: TimeSpan.FromSeconds(5));
        }

        dataSource = builder.Build();
    }

    public MySqlConnection CreateConnection() => dataSource.CreateConnection();

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
}

// Bound from ConnectionStrings:KlesiaDb (no Password) and validated at startup.
public sealed class DatabaseOptions
{
    [System.ComponentModel.DataAnnotations.Required]
    public string ConnectionString { get; set; } = string.Empty;
}

#endregion

#region Infrastructure/Persistence/Repositories/DemandRepository.cs

internal sealed class DemandRepository(IDbConnectionFactory connectionFactory) : IDemandRepository
{
    public async Task<Demand?> FindByIdAsync(int demandId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.Id AS DemandId, d.GKNumber, d.BaeLink, s.Code AS StatusCode
            FROM Demand AS d
            INNER JOIN ref_Status AS s ON s.Id = d.StatusId
            WHERE d.Id = @DemandId
            """;

        await using MySqlConnection connection = connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Demand>(
            new CommandDefinition(sql, new { DemandId = demandId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Demand>> ListByUserAsync(int userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.Id AS DemandId, d.GKNumber, d.BaeLink, s.Code AS StatusCode
            FROM Demand AS d
            INNER JOIN ref_Status AS s ON s.Id = d.StatusId
            WHERE d.UserId = @UserId
            ORDER BY d.Id DESC
            """;

        await using MySqlConnection connection = connectionFactory.CreateConnection();
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
        // MySQL has no OUTPUT / RETURNING: read LAST_INSERT_ID() on the SAME session, so open the connection explicitly.
        const string insertSql = """
            INSERT INTO Demand (UserId, GKNumber, BaeLink, StatusId)
            SELECT @UserId, @GKNumber, @BaeLink, s.Id
            FROM ref_Status AS s
            WHERE s.Code = 'pending'
            """;

        await using MySqlConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            int inserted = await connection.ExecuteAsync(
                new CommandDefinition(
                    insertSql,
                    new { UserId = userId, GKNumber = gkNumber, BaeLink = baeLink },
                    cancellationToken: cancellationToken));

            if (inserted != 1)
            {
                // The 'pending' status row is missing: reference data was not seeded.
                throw new InvalidOperationException("The 'pending' status is missing from ref_Status.");
            }

            return await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT LAST_INSERT_ID()", cancellationToken: cancellationToken));
        }
        catch (MySqlException ex) when (ex.Number is 1062 or 1452 or 3819 or 1048)
        {
            // 1062 duplicate key, 1452 missing parent row, 3819 check constraint violated, 1048 column cannot be null.
            // Translate it so Application never sees MySqlException.
            throw new InvalidOperationException("The demand violates a database constraint.", ex);
        }
    }

    public async Task<bool> UpdateStatusAsync(int demandId, string statusCode, CancellationToken cancellationToken)
    {
        // MySQL multi-table UPDATE: UPDATE ... JOIN ... SET ... WHERE (no UPDATE ... FROM).
        const string sql = """
            UPDATE Demand AS d
            INNER JOIN ref_Status AS s ON s.Code = @StatusCode
            SET d.StatusId = s.Id
            WHERE d.Id = @DemandId
            """;

        await using MySqlConnection connection = connectionFactory.CreateConnection();
        int affected = await connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new { DemandId = demandId, StatusCode = statusCode },
                cancellationToken: cancellationToken));

        // With MySqlConnector's default UseAffectedRows=false this counts matched rows,
        // so setting the status a demand already has still returns true.
        return affected > 0;
    }
}

#endregion

#region Multi-statement transaction (only when several statements must succeed together)

internal static class TransactionExample
{
    public static async Task ExecuteAsync(
        IDbConnectionFactory connectionFactory,
        CancellationToken cancellationToken)
    {
        await using MySqlConnection connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE Demand SET StatusId = @StatusId WHERE Id = @DemandId",
                new { StatusId = 2, DemandId = 1 },
                transaction,
                cancellationToken: cancellationToken));

            // ... further DML statements sharing the same transaction ...
            // DDL never belongs here: MySQL commits it implicitly (use a migration script instead).

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
