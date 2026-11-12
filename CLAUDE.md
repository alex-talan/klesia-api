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
- Use Key Vault and App Configuration for production configuration and secrets.
- Hide Azure-specific details behind interfaces when the application layer depends on them.

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
- The authoritative requirements are in `docs/specs/SPECIFICATIONS.md`; change contracts there first.
