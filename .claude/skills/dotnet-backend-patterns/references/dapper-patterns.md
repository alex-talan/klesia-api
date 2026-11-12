# Dapper Patterns and Best Practices (MySQL)

Advanced patterns for data access with Dapper over **MySqlConnector** against **Azure Database for MySQL 8.4**.

> The examples are shaped like the Klesia schema: tables `sec_Role`, `sec_User`, `ref_Status` and `Demand` in one database (MySQL has no schemas inside a database), keys named `Id`, foreign keys `{ReferencedTable}Id`. The schema is owned by the DbUp migrator in `database/Klesia.Database`. See `assets/repository-template.cs` for a complete repository and the `mysql-database` skill for the dialect rules.

## Why Dapper?

Klesia uses Dapper over `MySqlConnector`. There is no ORM: the migrator scripts (`database/Klesia.Database`) are the single owner of the schema, so there are no EF migrations, no `DbContext` and no change tracking.

Dapper gives you:

- Full control over the SQL (CTEs, window functions, index hints)
- Predictable performance and a small abstraction surface
- Explicit mapping between rows and DTOs/domain types
- Easy parameterization (`@Name`) to prevent SQL injection

You are responsible for:

- Writing and reviewing the SQL by hand, **in the MySQL dialect** (never T-SQL)
- Mapping joins and multiple result sets explicitly
- Keeping column names and types aligned with the migration scripts
- Managing transactions explicitly

## Connection Management

### 1. Proper Connection Handling

```csharp
// Klesia convention: a factory returns a MySqlConnection from a pooled MySqlDataSource.
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

        // Passwordless (spec DEC-06): without a Password, a Microsoft Entra access token is the password.
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

// Registration (Infrastructure/DependencyInjection.cs)
services.AddOptions<DatabaseOptions>()
    .Configure<IConfiguration>((options, configuration) =>
        options.ConnectionString = configuration.GetConnectionString("KlesiaDb") ?? string.Empty)
    .ValidateDataAnnotations()
    .ValidateOnStart();
services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
```

The connection string comes from configuration (appsettings.Local.json, environment variables, Key Vault / App Configuration in Azure) and carries **no password**. It is never hardcoded. Entra tokens live 5 to 60 minutes, hence the periodic refresh well inside that window. One public report describes intermittent `Access denied` spikes with `UsePeriodicPasswordProvider` on another cloud (mysql-net/MySqlConnector issue 1456): if it shows up, check token expiry and the refresh interval first.

### 2. Connection Lifecycle

```csharp
internal sealed class DemandRepository(IDbConnectionFactory connectionFactory)
{
    public async Task<Demand?> FindAsync(int demandId, CancellationToken cancellationToken)
    {
        // Dapper opens the connection when it is closed and closes it again; dispose it always.
        await using MySqlConnection connection = connectionFactory.CreateConnection();

        return await connection.QueryFirstOrDefaultAsync<Demand>(
            new CommandDefinition(
                "SELECT Id AS DemandId, GKNumber, BaeLink FROM Demand WHERE Id = @DemandId",
                new { DemandId = demandId },
                cancellationToken: cancellationToken));
    }
}
```

**Open the connection yourself** whenever several statements must share one session: session state such as `LAST_INSERT_ID()` or user variables is lost when Dapper closes and the pool resets a connection between two calls.

## Query Patterns

### 3. Basic CRUD Operations

```csharp
// SELECT single
Demand? demand = await connection.QueryFirstOrDefaultAsync<Demand>(
    "SELECT Id AS DemandId, GKNumber, BaeLink FROM Demand WHERE Id = @Id",
    new { Id = id });

// SELECT multiple (explicit column list, never SELECT *)
IEnumerable<Demand> demands = await connection.QueryAsync<Demand>(
    "SELECT Id AS DemandId, GKNumber, BaeLink FROM Demand WHERE UserId = @UserId ORDER BY Id DESC",
    new { UserId = userId });

// IN list: Dapper expands the collection, no table-valued parameter is needed (MySQL has none)
IEnumerable<Demand> byIds = await connection.QueryAsync<Demand>(
    "SELECT Id AS DemandId, GKNumber, BaeLink FROM Demand WHERE Id IN @Ids",
    new { Ids = ids });

// INSERT with identity return: MySQL has no OUTPUT / RETURNING.
// LAST_INSERT_ID() is per connection, so open it explicitly and use the same connection twice.
await connection.OpenAsync(cancellationToken);
int inserted = await connection.ExecuteAsync(new CommandDefinition(
    "INSERT INTO Demand (UserId, GKNumber, BaeLink, StatusId) VALUES (@UserId, @GKNumber, @BaeLink, @StatusId)",
    demand,
    cancellationToken: cancellationToken));
int newId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
    "SELECT LAST_INSERT_ID()",
    cancellationToken: cancellationToken));

// UPDATE (affected rows = matched rows with MySqlConnector's default UseAffectedRows=false)
int rowsAffected = await connection.ExecuteAsync(
    "UPDATE Demand SET StatusId = @StatusId WHERE Id = @Id",
    new { Id = id, StatusId = statusId });

// DELETE (the schema has no cascade: delete children first, or the foreign key rejects the statement)
await connection.ExecuteAsync("DELETE FROM Demand WHERE Id = @Id", new { Id = id });
```

### 4. Dynamic Query Building

Only the *shape* of the SQL may vary, and only from fixed strings in code. Values always travel as parameters.

```csharp
public async Task<IReadOnlyList<Demand>> SearchAsync(DemandSearchCriteria criteria, CancellationToken cancellationToken)
{
    StringBuilder sql = new("SELECT Id AS DemandId, GKNumber, BaeLink FROM Demand WHERE UserId = @UserId");
    DynamicParameters parameters = new();
    parameters.Add("UserId", criteria.UserId);

    if (criteria.StatusId.HasValue)
    {
        sql.Append(" AND StatusId = @StatusId");
        parameters.Add("StatusId", criteria.StatusId.Value);
    }

    if (!string.IsNullOrWhiteSpace(criteria.GKNumberPrefix))
    {
        sql.Append(" AND GKNumber LIKE @GKNumberPrefix");
        parameters.Add("GKNumberPrefix", $"{criteria.GKNumberPrefix}%");
    }

    // Pagination: LIMIT/OFFSET (not OFFSET ... FETCH NEXT)
    sql.Append(" ORDER BY Id DESC LIMIT @PageSize OFFSET @Offset");
    parameters.Add("PageSize", criteria.PageSize);
    parameters.Add("Offset", (criteria.Page - 1) * criteria.PageSize);

    await using MySqlConnection connection = connectionFactory.CreateConnection();
    IEnumerable<Demand> rows = await connection.QueryAsync<Demand>(
        new CommandDefinition(sql.ToString(), parameters, cancellationToken: cancellationToken));
    return rows.AsList();
}
```

Escape `%` and `_` in user text used inside `LIKE`. Sprint 1 returns the full demand list without pagination by specification.

### 5. Multi-Mapping (Joins)

```csharp
// One-to-one: alias keys so the split column is explicit and unambiguous.
const string sql = """
    SELECT d.Id AS DemandId, d.GKNumber, d.BaeLink,
           s.Id AS StatusId, s.Code, s.DisplayName
    FROM Demand AS d
    INNER JOIN ref_Status AS s ON s.Id = d.StatusId
    WHERE d.Id = @DemandId
    """;

IEnumerable<DemandRow> result = await connection.QueryAsync<DemandRow, StatusRow, DemandRow>(
    new CommandDefinition(sql, new { DemandId = demandId }, cancellationToken: cancellationToken),
    (demand, status) => demand with { Status = status },
    splitOn: "StatusId");
```

For one-to-many, group in code with a dictionary keyed by the parent id (and `LEFT JOIN` when the children may be absent), exactly as in the usual Dapper pattern. Always list columns explicitly: `SELECT p.*, c.*` makes `splitOn` fragile.

### 6. Multiple Result Sets

```csharp
const string sql = """
    SELECT COUNT(*) FROM Demand WHERE UserId = @UserId;

    SELECT Id AS DemandId, GKNumber, BaeLink
    FROM Demand
    WHERE UserId = @UserId
    ORDER BY Id DESC
    LIMIT @PageSize OFFSET @Offset;
    """;

await using MySqlConnection connection = connectionFactory.CreateConnection();
await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(
    new CommandDefinition(sql, new { UserId = userId, PageSize = pageSize, Offset = offset }, cancellationToken: cancellationToken));

int total = await grid.ReadSingleAsync<int>();
List<Demand> page = (await grid.ReadAsync<Demand>()).AsList();
```

## Advanced Patterns

### 7. Stored Procedures

Klesia does not use stored procedures (logic lives in C#, schema in migrations). If one is ever introduced it must be created by a migration script, called with `commandType: CommandType.StoredProcedure`, and documented in the specification first. Output parameters work through `DynamicParameters` with `ParameterDirection.Output`.

### 8. Transactions

```csharp
public async Task CreateWithAuditAsync(Demand demand, CancellationToken cancellationToken)
{
    await using MySqlConnection connection = connectionFactory.CreateConnection();
    await connection.OpenAsync(cancellationToken);
    await using MySqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

    try
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO Demand (UserId, GKNumber, BaeLink, StatusId) VALUES (@UserId, @GKNumber, @BaeLink, @StatusId)",
            demand,
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
```

Transactions cover **DML only**. MySQL DDL commits implicitly, which is why migration scripts hold one DDL object each.

### 9. Custom Type Handlers

MySqlConnector maps `CHAR(36)` to `Guid` (default `GuidFormat=Char36`) and `TINYINT(1)` to `bool`, so Klesia needs no handler for `UserUniqueId` or `IsActive`. A handler is only justified for a real custom mapping, for example JSON columns:

```csharp
public sealed class JsonTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    public override T Parse(object value) => JsonSerializer.Deserialize<T>((string)value)!;

    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        parameter.Value = JsonSerializer.Serialize(value);
        parameter.DbType = DbType.String;
    }
}

// Register once at startup
SqlMapper.AddTypeHandler(new JsonTypeHandler<DemandMetadata>());
```

## Error Translation

Translate database errors in Infrastructure; do not leak `MySqlException` to Application.

```csharp
try
{
    // ... Dapper call ...
}
catch (MySqlException ex) when (ex.Number is 1062 or 1452 or 1451 or 3819 or 1048)
{
    // 1062 duplicate key, 1452 child row has no parent, 1451 parent row is referenced,
    // 3819 check constraint violated, 1048 column cannot be null
    throw new InvalidOperationException("The change violates a database constraint.", ex);
}
```

Never put the SQL text, parameter values or the connection string in the translated message or in logs.

## Performance Tips

### 10. Use CommandDefinition for Cancellation

```csharp
IEnumerable<Demand> result = await connection.QueryAsync<Demand>(
    new CommandDefinition(
        commandText: "SELECT Id AS DemandId, GKNumber, BaeLink FROM Demand WHERE UserId = @UserId ORDER BY Id DESC",
        parameters: new { UserId = userId },
        cancellationToken: cancellationToken,
        commandTimeout: 30));
```

### 11. Buffered vs Unbuffered Queries

```csharp
// Buffered (default): loads all rows into memory
IEnumerable<Demand> all = await connection.QueryAsync<Demand>(sql, parameters);

// Unbuffered: streams rows (lower memory for large result sets)
await foreach (Demand demand in connection.QueryUnbufferedAsync<Demand>(sql, parameters))
{
    // process one at a time
}
```

### 12. Connection Pooling

Pooling is on by default in MySqlConnector and is per `MySqlDataSource`, so keep one data source (the singleton factory above). Tune through the connection string only when measurement justifies it: `MinimumPoolSize`, `MaximumPoolSize`, `ConnectionTimeout`. Keep `SslMode=Required` (or stricter) against Azure.

## Anti-Patterns to Avoid

```csharp
// BAD: SQL injection risk
string bad = $"SELECT Id FROM Demand WHERE GKNumber = '{userInput}'";

// GOOD: parameterized query
const string good = "SELECT Id FROM Demand WHERE GKNumber = @GKNumber";
await connection.QueryAsync<int>(good, new { GKNumber = userInput });

// BAD: not disposing the connection
MySqlConnection leaked = factory.CreateConnection();

// GOOD: always dispose
await using MySqlConnection connection = factory.CreateConnection();

// BAD: T-SQL on a MySQL database
const string tsql = "SELECT TOP 10 * FROM [dbo].[Demand] ORDER BY Id DESC";

// GOOD: MySQL dialect, explicit columns
const string mysql = "SELECT Id AS DemandId, GKNumber FROM Demand ORDER BY Id DESC LIMIT 10";

// BAD: reading LAST_INSERT_ID() after Dapper closed the connection (a different pooled session may answer)
await connection.ExecuteAsync(insertSql, demand);
int wrong = await connection.ExecuteScalarAsync<int>("SELECT LAST_INSERT_ID()");

// GOOD: OpenAsync first, so both statements run on the same session
await connection.OpenAsync(cancellationToken);
```
