# Claude Code Instructions for This Repository

This file is the Claude Code-specific instruction entrypoint. Cross-tool equivalents also exist:

- `AGENTS.md` for Codex-style and generic repository-aware agents
- `.github/copilot-instructions.md` for GitHub Copilot
- `prompts/` for local LLM workflows

## 1. Plan First
- Treat any non-trivial task as architectural work.
- Inspect the existing layer before changing it.
- Keep the generated solution minimal, coherent, and production-oriented.

## 2. Clean Architecture Is Mandatory
- Domain must not depend on Application, Infrastructure, or WebApi.
- Application contains use cases, DTOs, interfaces, validation, and orchestration.
- Infrastructure contains Dapper repositories, Azure clients, and external integrations.
- WebApi is the transport layer and composition root only.
- Do not move business rules into controllers, EF configurations, or Azure adapters.

## 3. .NET Engineering Rules
- The solution targets `net10.0`.
- Use constructor injection.
- Keep nullable reference types enabled.
- Use async/await correctly and pass cancellation tokens through I/O paths.
- Prefer small, explicit abstractions over framework-heavy indirection.
- Use strongly typed configuration bound from `appsettings`, environment variables, and cloud configuration sources.
- Keep generated code minimal and purposeful.

## 4. Data Access Rules (Dapper, no EF)
- Persistence uses Dapper with Microsoft.Data.SqlClient against Azure SQL Database (spec DEC-14). Do NOT add Entity Framework Core, a DbContext or EF migrations.
- The SQL database project (`database/Klesia.Database`) is the only owner of the schema; change tables there.
- Write parameterized SQL inside Infrastructure repositories; never concatenate user input into SQL.
- Repository interfaces live in Application; implementations live in Infrastructure.
- The demand list is returned in full, without pagination (see docs/specs/SPECIFICATIONS.md).

## 5. API Rules
- Keep controllers thin.
- Use DTOs for request and response payloads.
- Use versioned routes for public APIs.
- Return proper HTTP status codes and ProblemDetails for failures.
- Include health checks, OpenAPI, and structured logging.

## 6. Azure Rules
- Keep Azure SDK usage inside Infrastructure.
- Prefer Managed Identity and Entra ID auth patterns.
- Never hardcode secrets.
- Production settings are the App Service's application settings (`Section__Key`); use Key Vault references (and App Configuration, if adopted) for any secret or centrally managed setting. The API itself needs no secret.
- Hide Azure-specific details behind interfaces when the application layer depends on them.

### Hosting: Azure App Service (spec DEC-07; DEC-15 Container Apps is withdrawn)
- The API is deployed to **Azure App Service**. Do not add a Dockerfile, container ingress, forwarded-header workarounds or probe rules: they belonged to the withdrawn Container Apps decision. Read spec section 6.3 (HOST-01 to HOST-11) before touching hosting, pipeline or Bicep code.
- **Always On** must be enabled in production: the extraction runs in the API process after the response (EXT-01) and an unloaded app would stop it.
- HTTPS Only; the App Service Health check targets `GET /health` (anonymous, detail-free). Add no other route for it.
- Identity and configuration: one user-assigned managed identity selected by the application setting `AZURE_CLIENT_ID`; settings are application settings in the `Section__Key` form. The API needs no secret.
- Stateless instances: no PDF or state on the instance's disk; the extraction worker stops cleanly on shutdown (EXT-08).
- The SQL database project is never published at API startup: the GitHub Actions pipeline publishes it with `SqlPackage` before the deployment, so every schema change must stay backward compatible with the previous version.
- The DSI settings listed in OPEN-08 (plan and tier, network paths, instances, request limits) are not decided yet: never hard-code them, and report anything that depends on them as unverified.

### Documents: Azure Blob Storage, served only through the API (spec DEC-13, DEC-24)
- The PDFs live in a **private** Blob Storage container that only the API reads. SharePoint, Microsoft Graph and SAS links are not used: a SAS is a bearer token and cannot be tied to a user.
- `Demand.BaeLink` holds the blob URL without SAS or query string and is **internal**: never return it, never log it.
- `bae_link` (in `GET` and `POST /api/v1/demands`) is the API path `/api/v1/demands/{demand_id}/bae`, built from `Demand.Id`: stable, relative, no storage URL.
- `GET /api/v1/demands/{demand_id}/bae` (spec section 5.4) is the only way to read a PDF. Select the demand by its id **and** the caller's `sec.User.Id` in a single query; a missing and a foreign id give the same `404`. Stream the blob (`IBaeDocumentStore.OpenReadAsync`): no whole-file buffer, nothing written to disk. Headers: `application/pdf`, `Cache-Control: private, no-store`, `X-Content-Type-Options: nosniff`.
- Access to the storage uses the managed identity (*Storage Blob Data Contributor*). No account key, connection string or SAS; shared-key access is disabled. Never accept a URL or a blob name from the client; the blob name is `{oid}/{unique id}.pdf`, create-only.
- `Azure.Storage.Blobs` stays in Infrastructure behind `IBaeDocumentStore`. The frontend must fetch the route with the bearer token and show an object URL (a plain link cannot carry the token).

## 7. Testing Rules
- Use xUnit, FluentAssertions, and NSubstitute consistently.
- Add tests for positive and negative scenarios.
- Keep unit tests focused on business behavior.
- Use integration tests for the HTTP pipeline (signed test tokens, in-memory repositories).
- SQL constraint tests need a real Azure SQL / SQL Server database: set `KLESIA_TEST_SQL_CONNECTION` to run them.

## 8. Verification Before Finish
- Run build and relevant tests before declaring work complete.
- Review warnings, analyzer output, and architectural drift.
- Confirm project references still point inward only.
- Leave README and docs/specs/SPECIFICATIONS.md in sync with the code.
- Never claim that a deployment to Azure App Service, a storage permission or the document route was verified without having run it; report it as unverified.
- The authoritative requirements are in `docs/specs/SPECIFICATIONS.md`; change contracts there first.
