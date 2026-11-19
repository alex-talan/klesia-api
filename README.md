# KLESIA API

Backend of the KLESIA application: secure REST APIs on **.NET 10**, **Azure SQL Database** accessed with **Dapper**, authentication with **Microsoft Entra ID**, database-driven authorization and deployment as a container on **Azure Container Apps**. The specification in [`docs/specs/SPECIFICATIONS.md`](docs/specs/SPECIFICATIONS.md) describes the whole application.

**What it does:** `GET /api/v1/demands` returns the signed-in user's identity and only their own demands. `POST /api/v1/demands` copies a BAE PDF into SharePoint, stores the demand as `pending` and answers `201`. In the background the PDF is analyzed by Azure Document Intelligence, the extracted information is stored (`DemandInformation`, `DemandFormula`) and the demand becomes `ok` or `fail`. The owner is notified through Azure SignalR (`POST /api/v1/notifications/negotiate`) when the demand is stored and when the extraction is finished. The extracted data is not exposed by the API.

## Repository layout

```text
.
├── Klesia.sln
├── database/Klesia.Database     SQL database project (owns the schema; builds a DACPAC)
├── docs/                        SPECIFICATIONS.md, local-development.md, deployment-container-apps.md
├── src/
│   ├── Domain                   ApplicationUser, Demand, GKNumber
│   ├── Application              use cases (DemandService), DTOs, repository abstractions
│   ├── Infrastructure           Dapper repositories, SQL connection, SharePoint, Document Intelligence and SignalR adapters, extraction worker
│   └── WebApi                   controllers, Entra authentication, CORS, logging, composition root
├── tests/
│   ├── UnitTests
│   └── IntegrationTests
├── .claude/                     Claude Code agents, skills, hooks (settings.json)
├── CLAUDE.md  AGENTS.md  .github/copilot-instructions.md  prompts/
```

Dependencies point inward only: `WebApi → Infrastructure → Application → Domain`.

## Key decisions

- **No Entity Framework.** Data access is Dapper with parameterized SQL; the SQL project is the only owner of the schema.
- **Azure SQL Database** (DEC-14): Dapper over `Microsoft.Data.SqlClient`; the schemas `dbo`, `ref` and `sec` hold `Demand`, `DemandInformation`, `DemandFormula`, `ref.Status`, `sec.Role` and `sec.User`. Access is passwordless (`Authentication=Active Directory Default`).
- **.NET 10** (`global.json` pins the SDK).
- **Azure Container Apps** (DEC-15, replacing App Service): the API ships as a container image (plain HTTP on port 8080, non-root, TLS at the ingress, user-assigned managed identity, configuration by environment variables). The SQL project is published by a pipeline step (SqlPackage), not inside the image. See [`docs/deployment-container-apps.md`](docs/deployment-container-apps.md).
- **Authorization rule:** only active users present in `sec.User` are served, and only their own demands.
- **PDF links** are SharePoint URLs returned as stored (validated as HTTPS); SharePoint permissions protect the documents.
- **Extraction in the background** (DEC-17, DEC-18): after the `201`, a worker reads the PDF back from SharePoint, sends it to Azure Document Intelligence (endpoint and model id are configuration), validates the answer and stores it in one transaction. SharePoint location and account are configuration too.
- **Configuration, not code:** tenant, API client id, scope, CORS origins and the SQL connection string come from configuration files or environment variables.

## Getting started

Follow [`docs/local-development.md`](docs/local-development.md): configure `appsettings.Local.json`, sign in with `az login`, publish the database, run the API.

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

Dapper guidance lives in `.claude/skills/dotnet-backend-patterns` and the `dapper-engineer` agent (`.claude/agents/dapper-engineer.md`). Container Apps hosting rules live in `.claude/skills/azure-container-apps`.

## License

See [LICENSE](LICENSE).
