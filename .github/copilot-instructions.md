# GitHub Copilot Instructions

This repository is the KLESIA backend: ASP.NET Core on .NET 10, Clean Architecture, Dapper and Azure SQL. Requirements live in `docs/specs/SPECIFICATIONS.md`.

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

- Use Dapper with parameterized SQL; do not add Entity Framework Core, a DbContext or EF migrations.
- The SQL project `database/Klesia.Database` owns the schema.
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
