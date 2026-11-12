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

## 4. Data Access Rules (Dapper, MySQL, no EF)
- The database is **Azure Database for MySQL (Flexible Server, MySQL 8.4)** (spec DEC-14), not SQL Server. Never write T-SQL: no `TOP`, `[brackets]`, `ISNULL`, `SCOPE_IDENTITY`, `OUTPUT`, `MERGE` or `GO`. Use the `mysql-database` skill for the dialect rules.
- Persistence uses Dapper with MySqlConnector. Do NOT add Entity Framework Core, a DbContext or EF migrations, and do NOT add Microsoft.Data.SqlClient.
- MySQL has no schemas inside a database: the tables are `sec_Role`, `sec_User`, `ref_Status` and `Demand` in the single database `klesia`. Quote identifiers with backticks in scripts and spell them exactly as in the spec.
- The database migrator (`database/Klesia.Database`, DbUp) is the only owner of the schema. Change the schema with a NEW numbered script in `Migrations/`; never edit a script that has been applied. One DDL object per script (MySQL DDL is not transactional).
- Scripts in `Seeds/` run on every execution: keep them idempotent (`UPDATE` join plus `INSERT ... WHERE NOT EXISTS`); no `INSERT IGNORE`, no `ON DUPLICATE KEY UPDATE`. No semicolon inside SQL comments or literals (the DbUp splitter cuts on it).
- Write parameterized MySQL SQL inside Infrastructure repositories; never concatenate user input into SQL.
- Database access is passwordless: no `Password` in the connection string, the Microsoft Entra access token is the password (spec DEC-06).
- Repository interfaces live in Application; implementations live in Infrastructure. `IDbConnectionFactory` returns a `MySqlConnection`.
- Sprint 1 returns the full demand list without pagination (see docs/specs/SPECIFICATIONS.md).

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
- Production settings are the container app's environment variables; use Key Vault (and App Configuration, if adopted) for any secret or centrally managed setting. The API itself needs no secret.
- Hide Azure-specific details behind interfaces when the application layer depends on them.

### Hosting: Azure Container Apps (spec DEC-15, not App Service)
- The API is deployed to **Azure Container Apps**. DEC-15 supersedes the App Service hosting of DEC-07. Read spec section 6.3 (HOST-01 to HOST-12) before touching a Dockerfile, a pipeline, Bicep or hosting-related startup code.
- Image: multi-stage `Dockerfile` at the repository root, non-root user, plain HTTP on port `8080`, no Kestrel HTTPS or certificate, no secret, tenant value or `appsettings.Local.json` in the image (`.dockerignore`), tagged with the commit SHA (never `latest`).
- TLS ends at the ingress: register `UseForwardedHeaders` (`XForwardedFor`, `XForwardedProto`) first in the pipeline. The ingress CORS policy stays disabled; the API owns CORS (spec section 5.2).
- Probes: startup and readiness on `GET /health`, liveness is a TCP probe (a database outage must not restart replicas). Do not add routes for probes; `/health` stays anonymous and detail-free.
- Stateless replicas: no file written to the container disk, no in-memory state that affects correctness, console logging only (Log Analytics collects it).
- Identity and configuration: one user-assigned managed identity selected with the environment variable `AZURE_CLIENT_ID`; settings are environment variables in the `Section__Key` form. The API needs no secret.
- The DbUp migrator is not in the API image and never runs at API startup: the GitHub Actions pipeline runs it before the new revision is deployed, so every migration must stay backward compatible with the previous revision.
- The DSI settings listed in OPEN-08 (environment, network path to MySQL, ingress exposure, registry, replicas, body-size limit) are not decided yet: never hard-code them, and report anything that depends on them as unverified.
- Use the `azure-container-apps` skill and the `devops-engineer` and `azure-engineer` agents for this work.

## 7. Testing Rules
- Use xUnit, FluentAssertions, and NSubstitute consistently.
- Add tests for positive and negative scenarios.
- Keep unit tests focused on business behavior.
- Use integration tests for the HTTP pipeline (signed test tokens, in-memory repositories).
- Database constraint tests need a real, disposable MySQL 8.4 database already migrated by `database/Klesia.Database`: set `KLESIA_TEST_DB_CONNECTION` to run them.

## 8. Verification Before Finish
- Run build and relevant tests before declaring work complete.
- Review warnings, analyzer output, and architectural drift.
- Confirm project references still point inward only.
- Leave README and docs/specs/SPECIFICATIONS.md in sync with the code.
- For container or pipeline changes run `docker build` when Docker is available; otherwise report the image as unverified. Never claim a deployment to Azure Container Apps was verified without having run it.
- The authoritative requirements are in `docs/specs/SPECIFICATIONS.md`; change contracts there first.
