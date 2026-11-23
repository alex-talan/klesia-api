using Dapper;
using FluentAssertions;
using Klesia.Infrastructure.Options;
using Klesia.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace IntegrationTests.Database;

public sealed class DemandRepositoryTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("KLESIA_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("KLESIA_TEST_SQL_CONNECTION not set");

    [SqlFact]
    public async Task ListByUserAsync_ReturnsOnlyThatUsersDemandsNewestFirst()
    {
        var roleCode = $"demand-repository-{Guid.NewGuid():N}";
        var ownerOid = Guid.NewGuid();
        var otherOid = Guid.NewGuid();

        try
        {
            await using var setupConnection = new SqlConnection(ConnectionString);
            await setupConnection.OpenAsync();

            var roleId = await setupConnection.ExecuteScalarAsync<int>(
                "INSERT INTO sec.Role (Name, Code) OUTPUT INSERTED.Id VALUES (@Name, @Code);",
                new { Name = roleCode, Code = roleCode });
            var ownerId = await InsertUserAsync(setupConnection, roleId, ownerOid, "Repository test owner");
            var otherId = await InsertUserAsync(setupConnection, roleId, otherOid, "Repository test other");
            var pendingStatusId = await GetStatusIdAsync(setupConnection, "pending");
            var okStatusId = await GetStatusIdAsync(setupConnection, "ok");

            await InsertDemandAsync(setupConnection, ownerId, pendingStatusId, "GK260902-144743121");
            await InsertDemandAsync(setupConnection, otherId, okStatusId, "GK260902-144743122");
            await InsertDemandAsync(setupConnection, ownerId, okStatusId, "GK260902-144743123");

            var repository = new DemandRepository(
                new SqlConnectionFactory(Options.Create(new DatabaseOptions { KlesiaDb = ConnectionString })));

            var demands = await repository.ListByUserAsync(ownerId, CancellationToken.None);

            demands.Select(demand => demand.GKNumber.Value)
                .Should()
                .Equal("GK260902-144743123", "GK260902-144743121");
            demands.Select(demand => demand.Status.Code)
                .Should()
                .Equal("ok", "pending");
            demands.Should().OnlyContain(demand => demand.UserId == ownerId);
        }
        finally
        {
            await using var cleanupConnection = new SqlConnection(ConnectionString);
            await cleanupConnection.OpenAsync();
            await cleanupConnection.ExecuteAsync(
                "DELETE d FROM dbo.Demand AS d "
                + "INNER JOIN sec.[User] AS u ON u.Id = d.UserId "
                + "WHERE u.UserUniqueId IN (@OwnerOid, @OtherOid); "
                + "DELETE FROM sec.[User] WHERE UserUniqueId IN (@OwnerOid, @OtherOid); "
                + "DELETE FROM sec.Role WHERE Code = @RoleCode;",
                new { OwnerOid = ownerOid, OtherOid = otherOid, RoleCode = roleCode });
        }
    }

    [SqlFact]
    public async Task GetOwnedBaeLinkAsync_ReturnsLinkOnlyForMatchingDemandAndUser()
    {
        var roleCode = $"owned-link-repository-{Guid.NewGuid():N}";
        var ownerOid = Guid.NewGuid();
        var otherOid = Guid.NewGuid();
        var ownerBlobUrl = $"https://contoso.blob.core.windows.net/bae/{Guid.NewGuid():N}.pdf";

        try
        {
            await using var setupConnection = new SqlConnection(ConnectionString);
            await setupConnection.OpenAsync();

            var roleId = await setupConnection.ExecuteScalarAsync<int>(
                "INSERT INTO sec.Role (Name, Code) OUTPUT INSERTED.Id VALUES (@Name, @Code);",
                new { Name = roleCode, Code = roleCode });
            var ownerId = await InsertUserAsync(setupConnection, roleId, ownerOid, "Repository test owner");
            var otherId = await InsertUserAsync(setupConnection, roleId, otherOid, "Repository test other");
            var pendingStatusId = await GetStatusIdAsync(setupConnection, "pending");
            var ownedDemandId = await InsertDemandAsync(
                setupConnection,
                ownerId,
                pendingStatusId,
                "GK260902-144743124",
                ownerBlobUrl);
            var otherDemandId = await InsertDemandAsync(
                setupConnection,
                otherId,
                pendingStatusId,
                "GK260902-144743125",
                $"https://contoso.blob.core.windows.net/bae/{Guid.NewGuid():N}.pdf");

            var repository = new DemandRepository(
                new SqlConnectionFactory(Options.Create(new DatabaseOptions { KlesiaDb = ConnectionString })));

            (await repository.GetOwnedBaeLinkAsync(ownedDemandId, ownerId, CancellationToken.None))
                .Should()
                .Be(ownerBlobUrl);
            (await repository.GetOwnedBaeLinkAsync(otherDemandId, ownerId, CancellationToken.None))
                .Should()
                .BeNull();
            (await repository.GetOwnedBaeLinkAsync(int.MaxValue, ownerId, CancellationToken.None))
                .Should()
                .BeNull();
        }
        finally
        {
            await using var cleanupConnection = new SqlConnection(ConnectionString);
            await cleanupConnection.OpenAsync();
            await cleanupConnection.ExecuteAsync(
                "DELETE d FROM dbo.Demand AS d "
                + "INNER JOIN sec.[User] AS u ON u.Id = d.UserId "
                + "WHERE u.UserUniqueId IN (@OwnerOid, @OtherOid); "
                + "DELETE FROM sec.[User] WHERE UserUniqueId IN (@OwnerOid, @OtherOid); "
                + "DELETE FROM sec.Role WHERE Code = @RoleCode;",
                new { OwnerOid = ownerOid, OtherOid = otherOid, RoleCode = roleCode });
        }
    }

    private static async Task<int> InsertUserAsync(
        SqlConnection connection,
        int roleId,
        Guid oid,
        string name)
    {
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO sec.[User] (UserUniqueId, RoleId, Name, Email, IsInternal, IsActive) "
            + "OUTPUT INSERTED.Id VALUES (@Oid, @RoleId, @Name, @Email, 0, 1);",
            new { Oid = oid, RoleId = roleId, Name = name, Email = $"{oid:N}@example.test" });
    }

    private static async Task<int> GetStatusIdAsync(SqlConnection connection, string code)
    {
        return await connection.ExecuteScalarAsync<int>(
            "SELECT Id FROM ref.Status WHERE Code = @Code;",
            new { Code = code });
    }

    private static async Task<int> InsertDemandAsync(
        SqlConnection connection,
        int userId,
        int statusId,
        string gkNumber,
        string? baeLink = null)
    {
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Demand (UserId, GKNumber, BaeLink, StatusId) "
            + "OUTPUT INSERTED.Id VALUES (@UserId, @GKNumber, @BaeLink, @StatusId);",
            new
            {
                UserId = userId,
                GKNumber = gkNumber,
                BaeLink = baeLink ?? $"https://contoso.blob.core.windows.net/bae/{Guid.NewGuid():N}.pdf",
                StatusId = statusId,
            });
    }
}