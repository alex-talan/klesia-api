# Agent Instructions for This Repository

This file is the cross-tool instruction entrypoint for AI coding agents such as Codex, local LLM agents, and other repository-aware assistants.

## Primary Goal

Use this repository as a starter kit for building maintainable enterprise .NET services with:

- ASP.NET Core Web API
- Clean Architecture
- Dapper over MySqlConnector for data access (no Entity Framework) against Azure Database for MySQL, and a DbUp migrator project (`database/Klesia.Database`) for the schema
- Strong typing and nullable reference types
- Structured logging and health checks
- Azure-ready integration boundaries and a container image for Azure Container Apps
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

## Data Access Rules (Dapper, MySQL, no EF)

- The database is Azure Database for MySQL (Flexible Server, MySQL 8.4), not SQL Server: write MySQL-dialect SQL, never T-SQL.
- Do not add Entity Framework Core, a DbContext, EF migrations or Microsoft.Data.SqlClient. Use MySqlConnector.
- Tables are `sec_Role`, `sec_User`, `ref_Status`, `Demand` in one database (MySQL has no schemas); see `docs/specs/SPECIFICATIONS.md` section 3.
- Schema changes go in a NEW numbered script under `database/Klesia.Database/Migrations/` (never edit an applied one, one DDL object per script). Seeds in `Seeds/` must stay idempotent.
- Write parameterized SQL in Infrastructure repositories.
- Database access is passwordless: the Microsoft Entra access token is the MySQL password (no `Password` in the connection string).
- Repository interfaces live in Application, implementations in Infrastructure.
- Sprint 1 returns the full demand list without pagination (see `docs/specs/SPECIFICATIONS.md`).

## Azure Rules

- Keep Azure SDK usage inside Infrastructure.
- Prefer Managed Identity and Entra ID auth.
- Never hardcode secrets.
- Production settings are the container app's environment variables; use Key Vault (and App Configuration, if adopted) for any secret or centrally managed setting.
- Keep cloud-specific details behind boundaries when business logic depends on them.

## Hosting Rules (Azure Container Apps)

- The API is deployed to Azure Container Apps (specification DEC-15, which supersedes the App Service hosting of DEC-07). Read `docs/specs/SPECIFICATIONS.md` section 6.3 (HOST-01 to HOST-12) before touching hosting, Docker or pipeline files.
- Image: multi-stage `Dockerfile` at the repository root, non-root, plain HTTP on port 8080, no Kestrel HTTPS or certificate, no secret or `appsettings.Local.json` in the image, SHA tag (never `latest`).
- TLS ends at the ingress: `UseForwardedHeaders` (`XForwardedFor`, `XForwardedProto`) first in the pipeline; the ingress CORS policy stays off and the API owns CORS.
- Probes: startup and readiness on `GET /health`, liveness is TCP. Add no route for probes.
- Stateless replicas, console logging only, configuration through `Section__Key` environment variables, user-assigned managed identity selected with `AZURE_CLIENT_ID`.
- The DbUp migrator is not in the API image and never runs at startup: the GitHub Actions pipeline runs it before the new revision, so migrations stay backward compatible with the previous revision.
- OPEN-08 items (environment, network to MySQL, ingress, registry, replicas, body-size limit) are not decided: never hard-code them and report dependent work as unverified.
- Guidance: `.claude/skills/azure-container-apps/SKILL.md`.

## Testing Rules

- Use xUnit, FluentAssertions, and NSubstitute consistently.
- Add tests for both happy paths and failure paths.
- Keep unit tests focused on business behavior.
- Use integration tests for HTTP endpoints; database constraint tests run against a real, disposable MySQL 8.4 database when `KLESIA_TEST_DB_CONNECTION` is set.

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
