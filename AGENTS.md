# Agent Instructions for This Repository

This file is the cross-tool instruction entrypoint for AI coding agents such as Codex, local LLM agents, and other repository-aware assistants.

## Primary Goal

Use this repository as a starter kit for building maintainable enterprise .NET services with:

- ASP.NET Core Web API
- Clean Architecture
- Dapper for data access (no Entity Framework) and a SQL database project for the schema
- Strong typing and nullable reference types
- Structured logging and health checks
- Azure-ready integration boundaries
- High-quality automated tests

## Architecture Rules

- `src/Domain` must not depend on `Application`, `Infrastructure`, or `WebApi`.
- `src/Application` owns use cases, DTOs, interfaces, validation, and orchestration.
- `src/Infrastructure` owns Dapper repositories, Azure SDK clients, and implementation details.
- `src/WebApi` is the transport layer and composition root only.
- Keep business logic out of controllers, middleware, EF mappings, and cloud adapters.

## Engineering Rules

- The solution targets `net10.0`.
- Prefer constructor injection.
- Keep nullable reference types enabled.
- Use async/await correctly and pass cancellation tokens through I/O paths.
- Use strongly typed options instead of scattered configuration string lookups.
- Keep generated code small, explicit, and purposeful.

## API Rules

- Keep controllers thin.
- Use DTOs for request and response contracts.
- Version public APIs.
- Return proper HTTP status codes.
- Use `ProblemDetails` or `ValidationProblemDetails` for failures.
- Include health checks, OpenAPI, and structured logging.

## Data Access Rules (Dapper, no EF)

- Do not add Entity Framework Core, a DbContext or EF migrations.
- Schema changes go in the SQL database project `database/Klesia.Database`.
- Write parameterized SQL in Infrastructure repositories.
- Repository interfaces live in Application, implementations in Infrastructure.
- Sprint 1 returns the full demand list without pagination (see `docs/specs/SPECIFICATIONS.md`).

## Azure Rules

- Keep Azure SDK usage inside Infrastructure.
- Prefer Managed Identity and Entra ID auth.
- Never hardcode secrets.
- Use Key Vault and App Configuration for production secret and configuration management.
- Keep cloud-specific details behind boundaries when business logic depends on them.

## Testing Rules

- Use xUnit, FluentAssertions, and NSubstitute consistently.
- Add tests for both happy paths and failure paths.
- Keep unit tests focused on business behavior.
- Use integration tests for HTTP endpoints; SQL constraint tests run against a real database when `KLESIA_TEST_SQL_CONNECTION` is set.

## Verification Before Finish

- Run relevant build and test steps before declaring work complete.
- Review warnings, analyzer output, and architectural drift.
- Confirm project references still point inward only.
- Keep repository documentation in sync with the implementation.

## Cross-Tool Prompt Assets

This repository also contains a more detailed prompt library:

- `.claude/agents/` for specialized agent personas
- `.claude/skills/` for task-specific review and implementation guides
- `prompts/` for portable prompt entrypoints for local LLM workflows

If your tool does not automatically load `.claude/`, instruct it to read the relevant files directly.
