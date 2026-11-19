# GitHub Copilot Instructions

This repository is the KLESIA backend: ASP.NET Core on .NET 10, Clean Architecture, Dapper and Azure SQL, deployed on Azure App Service, with the PDFs in Azure Blob Storage. Requirements live in `docs/specs/SPECIFICATIONS.md`.

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

## Hosting (Azure App Service, spec DEC-07)

- The API runs on Azure App Service (the Container Apps decision is withdrawn): no Dockerfile, Always On enabled, HTTPS Only, Health check on `GET /health` and no other probe route.
- Application settings in `Section__Key` form, user-assigned managed identity (`AZURE_CLIENT_ID`), stateless instances, clean shutdown of the extraction worker.
- The SQL project is published by the pipeline (`SqlPackage`), never by the API; schema changes stay backward compatible with the previous version.
- Details: `docs/specs/SPECIFICATIONS.md` section 6.3 and `docs/deployment-app-service.md`.

## Documents (Azure Blob Storage, spec DEC-24)

- PDFs live in a private Blob container read only by the API. `Demand.BaeLink` (blob URL, no SAS) is internal and never returned.
- `bae_link` is the API path `/api/v1/demands/{demand_id}/bae`; that route streams the PDF to its owner (demand selected by id and caller id in one query, `404` otherwise). No SAS, no account key, no blob name or URL from the client.

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
