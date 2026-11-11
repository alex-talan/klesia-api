---
name: dotnet-backend-patterns
description: Klesia .NET 10 backend patterns - Clean Architecture layering, Dapper repositories over Microsoft.Data.SqlClient with ISqlConnectionFactory, validated typed options, async with cancellation tokens, domain exceptions mapped to ProblemDetails, and xUnit + FluentAssertions + NSubstitute tests. Entity Framework Core is not used. Use when implementing or reviewing backend features, repositories, services, or tests.
---

# .NET Backend Patterns (Klesia)

Patterns for the Klesia API: ASP.NET Core on `net10.0`, Clean Architecture, Dapper over Azure SQL, Microsoft Entra ID. The repository rules in `CLAUDE.md`, `AGENTS.md` and `docs/specs/SPECIFICATIONS.md` always win over this skill.

## When to Use This Skill

- Implementing a feature across Domain, Application, Infrastructure, and WebApi
- Writing or reviewing Dapper repositories and SQL
- Wiring DI and strongly typed configuration
- Mapping failures to HTTP responses
- Writing unit and integration tests

Related skills: `architecture-review` (boundaries), `api-contract-review` (HTTP contract), `security-audit` (auth and input), `azure-integration` (Azure SDKs, Managed Identity), `logging-patterns`, `testing-patterns`, `clean-code`.

## Fixed Decisions

| Topic | Rule |
|-------|------|
| Framework | `net10.0`, nullable enabled, warnings as errors (see `Directory.Build.props`) |
| Data access | Dapper + `Microsoft.Data.SqlClient`. No EF Core, no `DbContext`, no EF migrations |
| Schema | Owned by `database/Klesia.Database` (SQL project). Never change schema from application code |
| Repositories | Interfaces in Application, implementations in Infrastructure, parameterized SQL only |
| Errors | Domain/application exceptions mapped to `application/problem+json` by one exception handler |
| Tests | xUnit, FluentAssertions, NSubstitute. Integration tests use in-memory repositories and signed test tokens |
| Packages | Versions live in `Directory.Packages.props` (central package management) |

Not part of the stack: Redis, MCP servers, FluentValidation, Moq, MediatR. Do not introduce them from this skill. If a requirement needs one, record the decision in `docs/specs/SPECIFICATIONS.md` first.

## Core Concepts

### 1. Project Structure

```
src/
├── Domain/                      # Entities, value objects (no dependencies)
│   └── Demands/
├── Application/                 # Use cases, DTOs, abstractions
│   ├── Abstractions/            # IDemandRepository, IUserRepository, ...
│   ├── Common/Exceptions/       # NotFoundException, ForbiddenException
│   └── Demands/                 # Services and DTOs per feature
├── Infrastructure/              # Dapper, Azure clients, external integrations
│   ├── Options/                 # SqlServerOptions, AzureOptions
│   ├── Persistence/             # ISqlConnectionFactory
│   │   └── Repositories/        # Dapper repositories
│   └── DependencyInjection.cs   # AddInfrastructure(...)
└── WebApi/                      # Transport and composition root
    ├── Authentication/          # Entra ID, policies
    ├── Controllers/
    ├── Contracts/               # Request/response payloads
    ├── Infrastructure/          # GlobalExceptionHandler
    └── Program.cs
tests/
├── UnitTests/
└── IntegrationTests/
database/Klesia.Database/        # Tables, schemas, seed scripts (dbo, ref, sec)
```

Dependencies point inward only: WebApi -> Infrastructure -> Application -> Domain.

### 2. Dependency Injection and Options

Use constructor injection (primary constructors are fine) and register each layer from its own extension method.

```csharp
// Infrastructure/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SqlServerOptions>()
            .Bind(configuration.GetSection(SqlServerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDemandRepository, DemandRepository>();

        return services;
    }
}

// Infrastructure/Options/SqlServerOptions.cs
public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
```

- Bind settings to typed options and validate at startup (`ValidateOnStart`). The API must refuse to start with missing or placeholder values.
- Secrets never live in source. Use user secrets locally and Key Vault / App Configuration in Azure (see `azure-integration`).
- `IOptions<T>` for startup-time settings, `IOptionsMonitor<T>` only when live reload is required.

### 3. Async and Cancellation

```csharp
// CORRECT: async all the way, token passed to every I/O call
public async Task<UserDemandsDto> GetForUserAsync(Guid userUniqueId, CancellationToken cancellationToken)
{
    User user = await users.FindActiveByUniqueIdAsync(userUniqueId, cancellationToken)
        ?? throw new ForbiddenException("The caller is not an active user.");

    IReadOnlyList<Demand> items = await demands.ListByUserAsync(user.UserId, cancellationToken);
    return UserDemandsDto.From(user, items);
}

// CORRECT: independent I/O in parallel (each call needs its own connection)
Task<User?> userTask = users.FindActiveByUniqueIdAsync(id, cancellationToken);
Task<IReadOnlyList<Status>> statusTask = statuses.ListAsync(cancellationToken);
await Task.WhenAll(userTask, statusTask);

// WRONG: blocking on async
var result = GetAsync(id, cancellationToken).Result;

// WRONG: async void (except event handlers)
public async void Process() { }
```

Controllers receive the `CancellationToken` as an action parameter and pass it down. Do not use `ConfigureAwait(false)` in application code (ASP.NET Core has no sync context).

### 4. Error Handling: Exceptions to ProblemDetails

Business failures are exceptions declared in Application. One handler in WebApi maps them to `application/problem+json`. Controllers stay thin and contain no try/catch.

```csharp
// Application/Common/Exceptions
public sealed class NotFoundException(string message) : Exception(message);
public sealed class ForbiddenException(string message) : Exception(message);

// WebApi/Infrastructure/GlobalExceptionHandler.cs
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        (int status, string title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Access denied"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Path}", httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title },
            options: null,
            contentType: "application/problem+json",
            cancellationToken);

        return true;
    }
}

// Program.cs
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
app.UseExceptionHandler();
```

Rules:
- Never return `exception.Message` for unexpected (500) errors. Log details, return a generic title.
- Never log secrets, tokens, or full connection strings.
- Validate input at the boundary (value objects such as `GKNumber` guard their own invariants).

## Data Access Patterns (Dapper)

Read [references/dapper-patterns.md](references/dapper-patterns.md) for connection handling, transactions, multi-mapping, table-valued parameters and anti-patterns. A complete repository example is in [assets/repository-template.cs](assets/repository-template.cs), and a service example in [assets/service-template.cs](assets/service-template.cs).

```csharp
// Infrastructure/Persistence
public interface ISqlConnectionFactory
{
    SqlConnection CreateConnection();
}

// Infrastructure/Persistence
internal sealed class SqlConnectionFactory(IOptions<SqlServerOptions> options) : ISqlConnectionFactory
{
    public SqlConnection CreateConnection() => new(options.Value.ConnectionString);
}

// Infrastructure/Persistence/Repositories
internal sealed class DemandRepository(ISqlConnectionFactory connectionFactory) : IDemandRepository
{
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
}
```

Conventions:
- SQL lives in `const` raw string literals next to the method that runs it. Explicit column lists, no `SELECT *`.
- Always parameterize (`@UserId`). Never concatenate or interpolate user input into SQL text.
- Pass the token through `CommandDefinition`.
- Schemas are `dbo`, `ref`, `sec`; primary keys are named `Id` and foreign keys `{ReferencedTable}Id`; alias keys to the domain names in `SELECT` (`d.Id AS DemandId`). Bracket reserved names (`[sec].[User]`).
- Bound read queries (paging or a natural key). Sprint 1 returns the full demand list by specification.
- Map rows to Domain types or Application DTOs. Never return persistence shapes from controllers.
- Open one short-lived connection per repository call. Use an explicit transaction only when several statements must succeed together.
- Translate SQL errors (constraint violations, timeouts) in Infrastructure; do not leak `SqlException` to Application.

## Testing Patterns

xUnit + FluentAssertions + NSubstitute. See also the `testing-patterns` skill.

### Unit Tests

```csharp
public class DemandServiceTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IDemandRepository _demands = Substitute.For<IDemandRepository>();
    private readonly DemandService _sut;

    public DemandServiceTests()
    {
        _sut = new DemandService(_users, _demands, NullLogger<DemandService>.Instance);
    }

    [Fact]
    public async Task GetForUserAsync_ShouldReturnDemands_WhenUserIsActive()
    {
        Guid uniqueId = Guid.NewGuid();
        _users.FindActiveByUniqueIdAsync(uniqueId, Arg.Any<CancellationToken>())
            .Returns(new User(7, uniqueId, "Ada", "ada@example.com"));
        _demands.ListByUserAsync(7, Arg.Any<CancellationToken>())
            .Returns([new Demand(1, "123456789", "https://example.com/bae", "pending")]);

        UserDemandsDto result = await _sut.GetForUserAsync(uniqueId, CancellationToken.None);

        result.Demands.Should().ContainSingle();
        await _demands.Received(1).ListByUserAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetForUserAsync_ShouldThrowForbidden_WhenUserIsUnknown()
    {
        _users.FindActiveByUniqueIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        Func<Task> action = () => _sut.GetForUserAsync(Guid.NewGuid(), CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        await _demands.DidNotReceive().ListByUserAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("260902-144743112")]
    [InlineData("GK260902-14474311A")]
    public void GKNumber_ShouldReject_InvalidValues(string value)
    {
        Action action = () => GKNumber.Parse(value);

        action.Should().Throw<ArgumentException>();
    }
}
```

### Integration Tests (HTTP pipeline)

Replace persistence with in-memory fakes, and send signed test tokens so the real authentication and authorization pipeline runs.

```csharp
public class DemandsEndpointTests(KlesiaWebApplicationFactory factory) : IClassFixture<KlesiaWebApplicationFactory>
{
    [Fact]
    public async Task GetDemands_ShouldReturn401_WhenTokenIsMissing()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/v1/demands");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

// Support/KlesiaWebApplicationFactory.cs
public sealed class KlesiaWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDemandRepository>();
            services.AddSingleton<IDemandRepository, InMemoryDemandRepository>();
        });
    }
}
```

### SQL Tests

Constraints, mappings and queries are not covered by fakes. Run them against a real Azure SQL / SQL Server database created from the database project, gated by the environment variable `KLESIA_TEST_SQL_CONNECTION` (tests are skipped when it is not set).

## Best Practices

### DO

1. Keep layers inward-only and register each layer in its own extension method
2. Use constructor injection and strongly typed, validated options
3. Pass `CancellationToken` through every I/O path
4. Use Dapper with parameterized SQL in Infrastructure repositories
5. Use DTOs for request and response payloads and `ProblemDetails` for failures
6. Change the schema in `database/Klesia.Database` only
7. Cover positive and negative paths in unit tests; test SQL against a real database
8. Use `record` types for DTOs and immutable data

### DON'T

1. Don't add EF Core, a `DbContext`, or migrations
2. Don't block on async (`.Result`, `.Wait()`) or use `async void`
3. Don't concatenate or interpolate user input into SQL
4. Don't put SQL, `SqlConnection` or Azure SDK types in Application or Domain
5. Don't expose domain entities or SQL rows directly in the API
6. Don't catch `Exception` without logging or rethrowing
7. Don't hardcode secrets or configuration values
8. Don't create `new HttpClient()` manually (use `IHttpClientFactory`)

## Common Pitfalls

- **N+1 queries**: use a join, multi-mapping or `QueryMultiple` instead of a query per row
- **Connection leaks**: `await using` the connection from `ISqlConnectionFactory`
- **Missing indexes**: check query plans; add indexes in the database project
- **Over-fetching**: select only the columns the DTO needs
- **Silent null mapping**: column names and types must match the constructor or properties of the target type
- **Timeouts**: configure command and HTTP client timeouts deliberately
