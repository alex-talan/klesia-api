using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using System.Data;
using Xunit.Sdk;

namespace IntegrationTests.Database;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KLESIA_TEST_SQL_CONNECTION")))
        {
            Skip = "KLESIA_TEST_SQL_CONNECTION not set";
        }
    }
}

public sealed class SqlConstraintTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("KLESIA_TEST_SQL_CONNECTION")
        ?? throw new InvalidOperationException("KLESIA_TEST_SQL_CONNECTION not set");

    [SqlFact]
    public async Task NamedObjectsExist()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        var objects = (await connection.QueryAsync<string>("SELECT [name] FROM sys.objects WHERE [name] LIKE 'PK_%' OR [name] LIKE 'FK_%' OR [name] LIKE 'AK_%' OR [name] LIKE 'CK_%' OR [name] LIKE 'DF_%' UNION ALL SELECT [name] FROM sys.indexes WHERE [name] LIKE 'IX_%'")).ToHashSet();

        foreach (var name in new[]
        {
            "PK_Role", "PK_User", "PK_Status", "PK_Demand", "PK_DemandInformation", "PK_DemandFormula",
            "FK_User_Role", "FK_Demand_User", "FK_Demand_Status", "FK_DemandInformation_Demand", "FK_DemandFormula_DemandInformation",
            "AK_Role_Code", "AK_Status_Code", "AK_User_UserUniqueId", "AK_DemandInformation_DemandId",
            "CK_Demand_GKNumber", "CK_Demand_BaeLink", "CK_Status_Code",
            "CK_DemandInformation_Siren", "CK_DemandInformation_Siret", "CK_DemandInformation_SiretSiren",
            "CK_DemandInformation_HeadquarterNic", "CK_DemandInformation_HeadquarterZip", "CK_DemandInformation_EstablishmentZip",
            "CK_DemandInformation_NafCode", "CK_DemandInformation_Idcc", "CK_DemandInformation_ReferrerCode", "CK_DemandInformation_CoveredAmount",
            "CK_DemandFormula_Name", "IX_User_RoleId", "IX_Demand_UserId", "IX_Demand_StatusId", "IX_DemandFormula_DemandInformationId"
        })
        {
            objects.Should().Contain(name);
        }
    }

    [SqlFact]
    public async Task ForeignKeysRejectOrphans()
    {
        await WithRolledBackTransactionAsync(async (connection, transaction) =>
        {
            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [sec].[User] ([UserUniqueId],[RoleId],[Name],[Email],[IsInternal],[IsActive]) VALUES (@Oid,-2147483647,@Name,@Email,0,1)",
                new { Oid = Guid.NewGuid(), Name = "Orphan user", Email = "orphan@example.test" });

            var roleId = await InsertRoleAsync(connection, transaction, "orphan-role");
            var userId = await InsertUserAsync(connection, transaction, roleId, Guid.NewGuid(), "Orphan demand user", true, false);

            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [dbo].[Demand] ([UserId],[GKNumber],[BaeLink],[StatusId]) VALUES (-2147483647,@GK,@Link,1)",
                new { GK = "GK260902-144743112", Link = "https://contoso.blob.core.windows.net/bae/test.pdf" });

            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [dbo].[Demand] ([UserId],[GKNumber],[BaeLink],[StatusId]) VALUES (@UserId,@GK,@Link,-2147483647)",
                new { UserId = userId, GK = "GK260902-144743113", Link = "https://contoso.blob.core.windows.net/bae/test.pdf" });
        });
    }

    [SqlFact]
    public async Task UniqueAndDuplicateRulesApply()
    {
        await WithRolledBackTransactionAsync(async (connection, transaction) =>
        {
            var roleId = await InsertRoleAsync(connection, transaction, "unique-role");
            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [sec].[Role] ([Name],[Code]) VALUES (@Name,@Code)",
                new { Name = "Duplicate role", Code = "unique-role" });

            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [ref].[Status] ([Name],[DisplayName],[Code]) VALUES (@Name,@DisplayName,@Code)",
                new { Name = "Duplicate pending", DisplayName = "Duplicate pending", Code = "pending" });

            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [ref].[Status] ([Name],[DisplayName],[Code]) VALUES (@Name,@DisplayName,@Code)",
                new { Name = "Unsupported", DisplayName = "Unsupported", Code = "OK" });

            var pendingStatusId = await connection.ExecuteScalarAsync<int>("SELECT [Id] FROM [ref].[Status] WHERE [Code] = 'pending'", transaction: transaction);
            var oid = Guid.NewGuid();
            var userId = await InsertUserAsync(connection, transaction, roleId, oid, "Unique user", true, true);
            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [sec].[User] ([UserUniqueId],[RoleId],[Name],[Email],[IsInternal],[IsActive]) VALUES (@Oid,@RoleId,@Name,@Email,1,1)",
                new { Oid = oid, RoleId = roleId, Name = "Duplicate oid", Email = "duplicate@example.test" });

            var firstId = await InsertDemandAsync(connection, transaction, userId, pendingStatusId, "GK260902-144743112");
            var secondId = await InsertDemandAsync(connection, transaction, userId, pendingStatusId, "GK260902-144743112");
            firstId.Should().BeGreaterThan(0);
            secondId.Should().BeGreaterThan(0);

            await InsertDemandInformationAsync(connection, transaction, firstId);
            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [dbo].[DemandInformation] ([DemandId],[Brand],[Branch],[RiskType],[Siren],[Social],[LegalStatus],[NafCode],[Idcc],[CoveredCategory],[CoveredAmount],[EstablishmentSiret],[Contribution]) VALUES (@DemandId,@Brand,@Branch,@RiskType,@Siren,@Social,@LegalStatus,@NafCode,1,@Category,1,@Siret,@Contribution)",
                ValidDemandInformation(new { DemandId = firstId }));
        });
    }

    [SqlFact]
    public async Task DemandChecksRejectInvalidValues()
    {
        await WithRolledBackTransactionAsync(async (connection, transaction) =>
        {
            var roleId = await InsertRoleAsync(connection, transaction, "demand-check-role");
            var oid = Guid.NewGuid();
            var userId = await InsertUserAsync(connection, transaction, roleId, oid, "Demand check user", true, true);
            var pendingStatusId = await connection.ExecuteScalarAsync<int>("SELECT [Id] FROM [ref].[Status] WHERE [Code] = 'pending'", transaction: transaction);

            foreach (var gk in new[] { "gk260902-144743112", "GX260902-144743112", "GK26A902-144743112", "GK260902144743112", "GK260902-14474311", "GK260902-144743112 ", "GK260 02-144743112" })
            {
                await AssertViolationAsync(connection, transaction,
                    "INSERT INTO [dbo].[Demand] ([UserId],[GKNumber],[BaeLink],[StatusId]) VALUES (@UserId,@GK,@Link,@StatusId)",
                    new { UserId = userId, GK = gk, Link = "https://contoso.blob.core.windows.net/bae/test.pdf", StatusId = pendingStatusId });
            }

            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [dbo].[Demand] ([UserId],[GKNumber],[BaeLink],[StatusId]) VALUES (@UserId,@GK,@Link,@StatusId)",
                new { UserId = userId, GK = "GK260902-144743114", Link = "http://contoso.blob.core.windows.net/bae/test.pdf", StatusId = pendingStatusId });

            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [dbo].[Demand] ([UserId],[GKNumber],[BaeLink],[StatusId]) VALUES (@UserId,@GK,@Link,@StatusId)",
                new { UserId = userId, GK = "GK260902-144743115", Link = "https://contoso.blob.core.windows.net/bae/test.pdf", StatusId = -2147483647 });
        });
    }

    [SqlFact]
    public async Task ExtractionChecksAndFormulaMultiplicityApply()
    {
        await WithRolledBackTransactionAsync(async (connection, transaction) =>
        {
            var roleId = await InsertRoleAsync(connection, transaction, "extraction-role");
            var userId = await InsertUserAsync(connection, transaction, roleId, Guid.NewGuid(), "Extraction user", true, true);
            var pendingStatusId = await connection.ExecuteScalarAsync<int>("SELECT [Id] FROM [ref].[Status] WHERE [Code] = 'pending'", transaction: transaction);
            var demandId = await InsertDemandAsync(connection, transaction, userId, pendingStatusId, "GK260902-144743116");

            var informationId = await InsertDemandInformationAsync(connection, transaction, demandId);
            await connection.ExecuteAsync("INSERT INTO [dbo].[DemandFormula] ([DemandInformationId],[Name],[Detail]) VALUES (@InformationId,@Name,@Detail)", new { InformationId = informationId, Name = "BASE", Detail = "One" }, transaction);
            await connection.ExecuteAsync("INSERT INTO [dbo].[DemandFormula] ([DemandInformationId],[Name],[Detail]) VALUES (@InformationId,@Name,@Detail)", new { InformationId = informationId, Name = "OPTION", Detail = "Two" }, transaction);

            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [dbo].[DemandFormula] ([DemandInformationId],[Name]) VALUES (@InformationId,@Name)",
                new { InformationId = informationId, Name = "   " });

            await AssertViolationAsync(connection, transaction,
                "INSERT INTO [dbo].[DemandFormula] ([DemandInformationId],[Name]) VALUES (-2147483647,@Name)",
                new { Name = "Orphan formula" });

            foreach (var badInformation in new object[]
            {
                new { Siren = "12345678 ", EstablishmentSiret = "12345678900012", HeadquarterNic = (object?)null, HeadquarterZip = (object?)null, EstablishmentZip = (object?)null, NafCode = "5510Z", Idcc = 1, ReferrerCode = (object?)null, CoveredAmount = 1 },
                new { Siren = "123456789", EstablishmentSiret = "98765432100012", HeadquarterNic = (object?)null, HeadquarterZip = (object?)null, EstablishmentZip = (object?)null, NafCode = "5510Z", Idcc = 1, ReferrerCode = (object?)null, CoveredAmount = 1 },
                new { Siren = "123456789", EstablishmentSiret = "12345678900012", HeadquarterNic = "1234", HeadquarterZip = (object?)null, EstablishmentZip = (object?)null, NafCode = "5510Z", Idcc = 1, ReferrerCode = (object?)null, CoveredAmount = 1 },
                new { Siren = "123456789", EstablishmentSiret = "12345678900012", HeadquarterNic = (object?)null, HeadquarterZip = "1234", EstablishmentZip = (object?)null, NafCode = "5510Z", Idcc = 1, ReferrerCode = (object?)null, CoveredAmount = 1 },
                new { Siren = "123456789", EstablishmentSiret = "12345678900012", HeadquarterNic = (object?)null, HeadquarterZip = (object?)null, EstablishmentZip = "1234", NafCode = "5510Z", Idcc = 1, ReferrerCode = (object?)null, CoveredAmount = 1 },
                new { Siren = "123456789", EstablishmentSiret = "12345678900012", HeadquarterNic = (object?)null, HeadquarterZip = (object?)null, EstablishmentZip = (object?)null, NafCode = "5510z", Idcc = 1, ReferrerCode = (object?)null, CoveredAmount = 1 },
                new { Siren = "123456789", EstablishmentSiret = "12345678900012", HeadquarterNic = (object?)null, HeadquarterZip = (object?)null, EstablishmentZip = (object?)null, NafCode = "5510Z", Idcc = 10000, ReferrerCode = (object?)null, CoveredAmount = 1 },
                new { Siren = "123456789", EstablishmentSiret = "12345678900012", HeadquarterNic = (object?)null, HeadquarterZip = (object?)null, EstablishmentZip = (object?)null, NafCode = "5510Z", Idcc = 1, ReferrerCode = -1, CoveredAmount = 1 },
                new { Siren = "123456789", EstablishmentSiret = "12345678900012", HeadquarterNic = (object?)null, HeadquarterZip = (object?)null, EstablishmentZip = (object?)null, NafCode = "5510Z", Idcc = 1, ReferrerCode = (object?)null, CoveredAmount = 0 }
            })
            {
                var badDemandId = await InsertDemandAsync(connection, transaction, userId, pendingStatusId, "GK260902-144743119");
                var parameters = ValidDemandInformation(new { DemandId = badDemandId });
                foreach (var property in badInformation.GetType().GetProperties())
                {
                    ((Dictionary<string, object>)parameters)[property.Name] = property.GetValue(badInformation)!;
                }

                await AssertViolationAsync(connection, transaction,
                    "INSERT INTO [dbo].[DemandInformation] ([DemandId],[Brand],[Branch],[RiskType],[Siren],[ReferrerCode],[Social],[LegalStatus],[HeadquarterNic],[HeadquarterZip],[EstablishmentZip],[NafCode],[Idcc],[CoveredCategory],[CoveredAmount],[EstablishmentSiret],[Contribution]) VALUES (@DemandId,@Brand,@Branch,@RiskType,@Siren,@ReferrerCode,@Social,@LegalStatus,@HeadquarterNic,@HeadquarterZip,@EstablishmentZip,@NafCode,@Idcc,@Category,@Amount,@EstablishmentSiret,@Contribution)",
                    parameters);
            }
        });
    }

    [SqlFact]
    public async Task CreatedAtDefaultAndInactiveUserAreValid()
    {
        await WithRolledBackTransactionAsync(async (connection, transaction) =>
        {
            var roleId = await InsertRoleAsync(connection, transaction, "default-role");
            var userId = await InsertUserAsync(connection, transaction, roleId, Guid.NewGuid(), "Inactive no demand", true, false);
            userId.Should().BeGreaterThan(0);

            var pendingStatusId = await connection.ExecuteScalarAsync<int>("SELECT [Id] FROM [ref].[Status] WHERE [Code] = 'pending'", transaction: transaction);
            var demandId = await InsertDemandAsync(connection, transaction, userId, pendingStatusId, "GK260902-144743118");
            var createdAt = await connection.ExecuteScalarAsync<DateTime>("SELECT [CreatedAt] FROM [dbo].[Demand] WHERE [Id] = @Id", new { Id = demandId }, transaction);

            createdAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        });
    }

    private static async Task WithRolledBackTransactionAsync(Func<SqlConnection, IDbTransaction, Task> test)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            await test(connection, transaction);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task AssertViolationAsync(SqlConnection connection, IDbTransaction transaction, string sql, object parameters)
    {
        var act = () => connection.ExecuteAsync(sql, parameters, transaction);
        await act.Should().ThrowAsync<SqlException>().Where(error =>
            error.Number == 547 || error.Number == 2627 || error.Number == 2601);
    }

    private static async Task<int> InsertRoleAsync(SqlConnection connection, IDbTransaction transaction, string code)
    {
        return await connection.ExecuteScalarAsync<int>("INSERT INTO [sec].[Role] ([Name],[Code]) OUTPUT INSERTED.[Id] VALUES (@Name,@Code)", new { Name = code, Code = code }, transaction);
    }

    private static async Task<int> InsertUserAsync(SqlConnection connection, IDbTransaction transaction, int roleId, Guid oid, string name, bool isInternal, bool isActive)
    {
        return await connection.ExecuteScalarAsync<int>("INSERT INTO [sec].[User] ([UserUniqueId],[RoleId],[Name],[Email],[IsInternal],[IsActive]) OUTPUT INSERTED.[Id] VALUES (@Oid,@RoleId,@Name,@Email,@IsInternal,@IsActive)", new { Oid = oid, RoleId = roleId, Name = name, Email = $"{Guid.NewGuid():N}@example.test", IsInternal = isInternal, IsActive = isActive }, transaction);
    }

    private static async Task<int> InsertDemandAsync(SqlConnection connection, IDbTransaction transaction, int userId, int statusId, string gk)
    {
        return await connection.ExecuteScalarAsync<int>("INSERT INTO [dbo].[Demand] ([UserId],[GKNumber],[BaeLink],[StatusId]) OUTPUT INSERTED.[Id] VALUES (@UserId,@GK,@Link,@StatusId)", new { UserId = userId, GK = gk, Link = "https://contoso.blob.core.windows.net/bae/test.pdf", StatusId = statusId }, transaction);
    }

    private static async Task<int> InsertDemandInformationAsync(SqlConnection connection, IDbTransaction transaction, int demandId)
    {
        return await connection.ExecuteScalarAsync<int>("INSERT INTO [dbo].[DemandInformation] ([DemandId],[Brand],[Branch],[RiskType],[Siren],[Social],[LegalStatus],[NafCode],[Idcc],[CoveredCategory],[CoveredAmount],[EstablishmentSiret],[Contribution]) OUTPUT INSERTED.[Id] VALUES (@DemandId,@Brand,@Branch,@RiskType,@Siren,@Social,@LegalStatus,@NafCode,1,@Category,1,@Siret,@Contribution)", ValidDemandInformation(new { DemandId = demandId }), transaction);
    }

    private static object ValidDemandInformation(object demandIdParameters)
    {
        var values = new Dictionary<string, object>
        {
            ["Brand"] = "HCR Santé",
            ["Branch"] = "CCN test",
            ["RiskType"] = "PRÉVOYANCE",
            ["Siren"] = "123456789",
            ["Social"] = "TEST HOLDING",
            ["LegalStatus"] = "SAS",
            ["NafCode"] = "5510Z",
            ["Category"] = "Non cadres",
            ["Siret"] = "12345678900012",
            ["Contribution"] = "0,85% TA",
            ["Idcc"] = 1,
            ["Amount"] = 1
        };

        foreach (var property in demandIdParameters.GetType().GetProperties())
        {
            values[property.Name] = property.GetValue(demandIdParameters)!;
        }

        return values;
    }
}
