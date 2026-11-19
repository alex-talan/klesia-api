# Agent Instructions for This Repository

This file is the cross-tool instruction entrypoint for AI coding agents such as Codex, local LLM agents, and other repository-aware assistants.

## Primary Goal

Use this repository as a starter kit for building maintainable enterprise .NET services with:

- ASP.NET Core Web API
- Clean Architecture
- Dapper for data access (no Entity Framework) and a SQL database project for the schema
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

## Data Access Rules (Dapper, no EF)

- Do not add Entity Framework Core, a DbContext or EF migrations.
- Schema changes go in the SQL database project `database/Klesia.Database`.
- Write parameterized SQL in Infrastructure repositories.
- Repository interfaces live in Application, implementations in Infrastructure.
- The demand list is returned in full, without pagination (see `docs/specs/SPECIFICATIONS.md`).

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
- The SQL database project is not in the API image and is never published at startup: the GitHub Actions pipeline publishes it with `SqlPackage` before the new revision, so schema changes stay backward compatible with the previous revision.
- OPEN-08 items (environment, network to Azure SQL Database, ingress, registry, replicas, body-size limit) are not decided: never hard-code them and report dependent work as unverified.
- Guidance: `.claude/skills/azure-container-apps/SKILL.md`.

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
