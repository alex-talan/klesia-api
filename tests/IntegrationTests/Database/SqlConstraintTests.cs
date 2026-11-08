using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace IntegrationTests.Database;

/// <summary>
/// Acceptance scenarios AT-15 and AT-16 against a real SQL Server / Azure SQL database.
/// Every test runs inside a transaction that is rolled back, so no data is kept.
/// </summary>
public sealed class SqlConstraintTests : IAsyncLifetime
{
    private const int ConstraintViolation = 547;
    private const int UniqueViolation = 2627;

    private SqlConnection? connection;
    private SqlTransaction? transaction;

    public async Task InitializeAsync()
    {
        string? connectionString = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        transaction = (SqlTransaction)await connection.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync();
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    [SqlServerFact]
    public async Task DuplicateRoleCode_IsRejected()
    {
        string code = Guid.NewGuid().ToString("N");
        await InsertRoleAsync(code);

        SqlException error = await Assert.ThrowsAsync<SqlException>(() => InsertRoleAsync(code));

        error.Number.Should().Be(UniqueViolation);
    }

    [SqlServerFact]
    public async Task UserWithUnknownRole_IsRejected()
    {
        SqlException error = await Assert.ThrowsAsync<SqlException>(() => InsertUserAsync(roleId: -1));

        error.Number.Should().Be(ConstraintViolation);
    }

    [SqlServerFact]
    public async Task DemandWithUnknownUserOrStatus_IsRejected()
    {
        int userId = await InsertUserAsync(await InsertRoleAsync(Guid.NewGuid().ToString("N")));
        int statusId = await PendingStatusIdAsync();

        SqlException orphanUser = await Assert.ThrowsAsync<SqlException>(() => InsertDemandAsync(-1, statusId, "732829320", "https://a.example/x.pdf"));
        SqlException orphanStatus = await Assert.ThrowsAsync<SqlException>(() => InsertDemandAsync(userId, -1, "732829320", "https://a.example/x.pdf"));

        orphanUser.Number.Should().Be(ConstraintViolation);
        orphanStatus.Number.Should().Be(ConstraintViolation);
    }

    [SqlServerFact]
    public async Task UnsupportedStatusCode_IsRejected()
    {
        SqlException error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(
            "INSERT [ref].[Status] ([Name], [DisplayName], [Code]) VALUES (N'x', N'x', N'unknown');"));

        error.Number.Should().Be(ConstraintViolation);
    }

    [SqlServerFact]
    public async Task InvalidSirenNumber_IsRejected()
    {
        int userId = await InsertUserAsync(await InsertRoleAsync(Guid.NewGuid().ToString("N")));
        int statusId = await PendingStatusIdAsync();

        foreach (string siren in new[] { "12345678", "12345678A", "123 56789" })
        {
            SqlException error = await Assert.ThrowsAsync<SqlException>(
                () => InsertDemandAsync(userId, statusId, siren, "https://a.example/x.pdf"));

            error.Number.Should().Be(ConstraintViolation, $"'{siren}' is not nine digits");
        }
    }

    [SqlServerFact]
    public async Task NonHttpsBaeLink_IsRejected()
    {
        int userId = await InsertUserAsync(await InsertRoleAsync(Guid.NewGuid().ToString("N")));
        int statusId = await PendingStatusIdAsync();

        SqlException error = await Assert.ThrowsAsync<SqlException>(
            () => InsertDemandAsync(userId, statusId, "732829320", "http://a.example/x.pdf"));

        error.Number.Should().Be(ConstraintViolation);
    }

    [SqlServerFact]
    public async Task SameSirenNumberOnSeveralDemands_IsAllowed()
    {
        int userId = await InsertUserAsync(await InsertRoleAsync(Guid.NewGuid().ToString("N")));
        int statusId = await PendingStatusIdAsync();

        await InsertDemandAsync(userId, statusId, "732829320", "https://a.example/1.pdf");
        await InsertDemandAsync(userId, statusId, "732829320", "https://a.example/2.pdf");

        int count = await ScalarAsync<int>(
            "SELECT COUNT(*) FROM [dbo].[Demand] WHERE [UserId] = @UserId;",
            new SqlParameter("@UserId", userId));
        count.Should().Be(2);
    }

    [SqlServerFact]
    public async Task InactiveUserWithoutDemands_CanExist()
    {
        int userId = await InsertUserAsync(await InsertRoleAsync(Guid.NewGuid().ToString("N")), isActive: false);

        userId.Should().BePositive();
    }

    private Task<int> InsertRoleAsync(string code) =>
        ScalarAsync<int>(
            "INSERT [sec].[Role] ([Name], [Code]) OUTPUT INSERTED.[RoleId] VALUES (N'Test role', @Code);",
            new SqlParameter("@Code", code));

    private Task<int> InsertUserAsync(int roleId, bool isActive = true) =>
        ScalarAsync<int>(
            """
            INSERT [sec].[User] ([UserUniqueId], [RoleId], [Name], [Email], [IsInternal], [IsActive])
            OUTPUT INSERTED.[UserId]
            VALUES (NEWID(), @RoleId, N'Test user', N'test.user@example.com', 0, @IsActive);
            """,
            new SqlParameter("@RoleId", roleId),
            new SqlParameter("@IsActive", isActive));

    private Task<int> InsertDemandAsync(int userId, int statusId, string siren, string link) =>
        ScalarAsync<int>(
            """
            INSERT [dbo].[Demand] ([UserId], [SirenNumber], [BaeLink], [StatusId])
            OUTPUT INSERTED.[DemandId]
            VALUES (@UserId, @Siren, @Link, @StatusId);
            """,
            new SqlParameter("@UserId", userId),
            new SqlParameter("@Siren", siren),
            new SqlParameter("@Link", link),
            new SqlParameter("@StatusId", statusId));

    private Task<int> PendingStatusIdAsync() =>
        ScalarAsync<int>("SELECT [StatusId] FROM [ref].[Status] WHERE [Code] = N'pending';");

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
