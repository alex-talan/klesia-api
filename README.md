# KLESIA API

Backend of the KLESIA application: secure REST APIs on **.NET 10**, **Azure SQL Database** accessed with **Dapper**, authentication with **Microsoft Entra ID**, database-driven authorization, PDFs in **Azure Blob Storage** and deployment on **Azure App Service**. The specification in [`docs/specs/SPECIFICATIONS.md`](docs/specs/SPECIFICATIONS.md) describes the whole application.

**What it does:** `GET /api/v1/demands` returns the signed-in user's identity and only their own demands; `GET /api/v1/demands/{demand_id}/bae` streams the PDF of one of those demands to its owner. `POST /api/v1/demands` copies a BAE PDF into Azure Blob Storage, stores the demand as `pending` and answers `201`. In the background the PDF is analyzed by Azure Document Intelligence, the extracted information is stored (`DemandInformation`, `DemandFormula`) and the demand becomes `ok` or `fail`. The owner is notified through Azure SignalR (`POST /api/v1/notifications/negotiate`) when the demand is stored and when the extraction is finished. The extracted data is not exposed by the API.

## Repository layout

```text
.
├── Klesia.sln
├── database/Klesia.Database     SQL database project (owns the schema; builds a DACPAC)
├── docs/                        SPECIFICATIONS.md, local-development.md, deployment-app-service.md
├── src/
│   ├── Domain                   ApplicationUser, Demand, GKNumber
│   ├── Application              use cases (DemandService), DTOs, repository abstractions
│   ├── Infrastructure           Dapper repositories, SQL connection, Blob Storage, Document Intelligence and SignalR adapters, extraction worker
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
- **Azure App Service** (DEC-07; the Container Apps decision DEC-15 is withdrawn): the API runs as a web app with Always On, HTTPS Only and the Health check on `/health`, a user-assigned managed identity and application settings. The SQL project is published by a pipeline step (SqlPackage). See [`docs/deployment-app-service.md`](docs/deployment-app-service.md).
- **Authorization rule:** only active users present in `sec.User` are served, and only their own demands.
- **PDF access** (DEC-24): the PDFs are private blobs that only the API can read. The database keeps the blob URL (never returned); `bae_link` is the API path `/api/v1/demands/{demand_id}/bae`, which streams the PDF to the owner of the demand and answers `404` for anyone else. No SAS and no link to the storage exist.
- **Extraction in the background** (DEC-17, DEC-18): after the `201`, a worker reads the PDF back from the blob storage, sends it to Azure Document Intelligence (endpoint and model id are configuration), validates the answer and stores it in one transaction. The storage account and container are configuration too.
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

Dapper guidance lives in `.claude/skills/dotnet-backend-patterns` and the `dapper-engineer` agent (`.claude/agents/dapper-engineer.md`).

## License

See [LICENSE](LICENSE).

## Publish the database project
VS Code parameters:
- Authentication type: Universal with MFA Support
- Account: Talan FR - Demo account (Azure AD)

```powershell
SqlPackage `
  /Action:Publish `
  /SourceFile:"database/Klesia.Database/bin/Debug/Klesia.Database.dacpac" `
  /Profile:"database/Klesia.Database/Properties/Azure.publish.xml" `
  /TargetConnectionString:'Data Source=klesia-dev-server-db.database.windows.net;Initial Catalog=klesia-dev-db;Authentication=Active Directory Interactive;User ID=alejandro.reyes-amaro@talan.com;Pooling=False;Connect Timeout=60;Encrypt=True;TrustServerCertificate=False;Application Name=SqlPackage;Application Intent=ReadWrite;Command Timeout=30'
```