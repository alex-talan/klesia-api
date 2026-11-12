# KLESIA API

Backend of the KLESIA application: secure REST APIs on **.NET 10**, **Azure Database for MySQL** (Flexible Server, MySQL 8.4) accessed with **Dapper**, authentication with **Microsoft Entra ID**, database-driven authorization and deployment as a container on **Azure Container Apps**. Development is incremental (Agile): the specification in [`docs/specs/SPECIFICATIONS.md`](docs/specs/SPECIFICATIONS.md) is delivered sprint by sprint.

**Sprint 1:** `GET /api/v1/demands` returns the signed-in user's identity and only their own demands. The SignalR negotiation (`POST /api/v1/notifications/negotiate`) and the notification publisher (notification *demand stored*, DEC-16) are in Sprint 1 too. `POST /api/v1/demands` with PDF upload (contract already adopted in `contracts/openapi.yaml`) and the OCR *extraction completed* notification are deferred to later sprints.

## Repository layout

```text
.
├── Klesia.sln
├── database/Klesia.Database     DbUp migrator + SQL scripts (owns the schema; applies it to MySQL)
├── docs/                        SPECIFICATIONS.md, local-development.md, deployment-container-apps.md
├── src/
│   ├── Domain                   ApplicationUser, Demand, GKNumber
│   ├── Application              use cases (DemandService), DTOs, repository abstractions
│   ├── Infrastructure           Dapper repositories, MySQL connection, SharePoint link resolver
│   └── WebApi                   controllers, Entra authentication, CORS, logging, composition root
├── tests/
│   ├── UnitTests
│   └── IntegrationTests
├── .claude/                     Claude Code agents, skills, hooks (settings.json)
├── CLAUDE.md  AGENTS.md  .github/copilot-instructions.md  prompts/
```

Dependencies point inward only: `WebApi → Infrastructure → Application → Domain`.

## Key decisions

- **No Entity Framework.** Data access is Dapper over MySqlConnector with parameterized MySQL-dialect SQL; the migration scripts in `database/Klesia.Database` are the only owner of the schema.
- **Azure Database for MySQL** (DEC-14): one database `klesia` with the tables `sec_Role`, `sec_User`, `ref_Status`, `Demand` (MySQL has no schemas inside a database). Access is passwordless: a Microsoft Entra access token is the MySQL password.
- **.NET 10** (`global.json` pins the SDK).
- **Azure Container Apps** (DEC-15, replacing App Service): the API ships as a container image (plain HTTP on port 8080, non-root, TLS at the ingress, user-assigned managed identity, configuration by environment variables). The DbUp migrator runs as a pipeline step, not inside the image. See [`docs/deployment-container-apps.md`](docs/deployment-container-apps.md).
- **Authorization rule:** only active users present in `sec_User` are served, and only their own demands.
- **PDF links** are SharePoint URLs returned as stored (validated as HTTPS); SharePoint permissions protect the documents.
- **Configuration, not code:** tenant, API client id, scope, CORS origins and the database connection string come from configuration files or environment variables.

## Getting started

Follow [`docs/local-development.md`](docs/local-development.md): configure `appsettings.Local.json`, sign in with `az login`, apply the database migrations, run the API.

```powershell
dotnet restore
dotnet build Klesia.sln
dotnet test Klesia.sln
dotnet run --project src/WebApi --launch-profile https
```

## Working with AI tools

| Tool | Entry point |
|---|---|
| Claude Code | `CLAUDE.md`, `.claude/` (agents, skills, hooks) |
| Codex / repository-aware agents | `AGENTS.md` |
| GitHub Copilot | `.github/copilot-instructions.md` |
| Local LLM tools | `prompts/` |

Dapper guidance lives in `.claude/skills/dotnet-backend-patterns` and the `dapper-engineer` agent (`.claude/agents/dapper-engineer.md`). MySQL schema, migration and Entra-authentication rules live in `.claude/skills/mysql-database`. Container Apps hosting rules live in `.claude/skills/azure-container-apps`.

## License

See [LICENSE](LICENSE).
