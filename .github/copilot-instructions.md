# GitHub Copilot Instructions

This repository is the KLESIA backend: ASP.NET Core on .NET 10, Clean Architecture, Dapper and Azure Database for MySQL. Requirements live in `docs/specs/SPECIFICATIONS.md`.

## Follow These Rules

- Preserve Clean Architecture boundaries.
- Keep `Domain` free of framework, data-access, Azure SDK, and HTTP concerns.
- Keep `Application` focused on use cases, contracts, validation, and orchestration.
- Keep `Infrastructure` focused on Dapper repositories, external integrations, and implementation details.
- Keep `WebApi` thin and transport-oriented.

## Coding Defaults

- Prefer explicit, readable C# over clever shortcuts.
- Use constructor injection.
- Keep nullable reference types enabled.
- Use async/await correctly and propagate cancellation tokens.
- Use strongly typed options for configuration.
- Use DTOs for HTTP contracts.
- Use `ProblemDetails` for failures.

## Data and Persistence

- Use Dapper over MySqlConnector with parameterized MySQL-dialect SQL (never T-SQL); do not add Entity Framework Core, a DbContext, EF migrations or Microsoft.Data.SqlClient.
- The database migrator `database/Klesia.Database` (DbUp) owns the schema: add a new numbered script in `Migrations/`, never edit an applied one.
- Tables are `sec_Role`, `sec_User`, `ref_Status`, `Demand` (MySQL has no schemas). Database access is passwordless (Entra token as password).
- Repository interfaces live in Application; implementations live in Infrastructure.

## Testing

- Add tests for positive and negative scenarios.
- Use xUnit, FluentAssertions, and NSubstitute.
- Use integration tests for real HTTP and persistence behavior when appropriate.

## Documentation and Verification

- Keep README and prompt assets aligned with code changes.
- Prefer minimal, maintainable changes over broad rewrites.
- Verify build and test impact when making non-trivial changes.

## Extra Prompt Assets

For deeper guidance, consult:

- `AGENTS.md`
- `CLAUDE.md`
- `.claude/agents/`
- `.claude/skills/`
- `prompts/README.md`
