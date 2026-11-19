# Agent Instructions for This Repository

This file is the cross-tool instruction entrypoint for AI coding agents such as Codex, local LLM agents, and other repository-aware assistants.

## Primary Goal

Use this repository as a starter kit for building maintainable enterprise .NET services with:

- ASP.NET Core Web API
- Clean Architecture
- Dapper for data access (no Entity Framework) and a SQL database project for the schema
- Strong typing and nullable reference types
- Structured logging and health checks
- Azure-ready integration boundaries and deployment to Azure App Service
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
- Production settings are the App Service's application settings; use Key Vault references (and App Configuration, if adopted) for any secret or centrally managed setting.
- Keep cloud-specific details behind boundaries when business logic depends on them.

## Hosting Rules (Azure App Service)

- The API is deployed to Azure App Service (specification DEC-07; the Container Apps decision DEC-15 is withdrawn). Read `docs/specs/SPECIFICATIONS.md` section 6.3 (HOST-01 to HOST-11) before touching hosting or pipeline files. Add no Dockerfile.
- Always On is required (the extraction runs in the API process, EXT-01). HTTPS Only. The Health check targets `GET /health`; add no other probe route.
- Application settings in `Section__Key` form; one user-assigned managed identity selected with `AZURE_CLIENT_ID`.
- Stateless instances, nothing written to the instance's disk, clean shutdown of the extraction worker (EXT-08).
- The SQL database project is published by the GitHub Actions pipeline (`SqlPackage`) before the deployment, never by the API; schema changes stay backward compatible with the previous version.
- OPEN-08 items are not decided: never hard-code them and report dependent work as unverified.

## Document Storage Rules (Azure Blob Storage, DEC-13, DEC-24)

- PDFs live in a private Blob Storage container that only the API reads (no SharePoint, no Microsoft Graph, no SAS links). `Demand.BaeLink` holds the blob URL without SAS and is internal: never returned, never logged.
- `bae_link` in `GET` and `POST /api/v1/demands` is the API path `/api/v1/demands/{demand_id}/bae`. That route (spec section 5.4) is the only way to read a PDF: it selects the demand by its id and the caller's id in one query, answers `404` for a missing or foreign id, and streams the blob (no whole-file buffer, nothing on disk).
- Storage access uses the managed identity. No account key, connection string or SAS. Never accept a URL or blob name from the client. Azure SDK code stays in Infrastructure behind `IBaeDocumentStore`.

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
