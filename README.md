# KLESIA API

Backend of the KLESIA application: secure REST APIs on **.NET 10**, **Azure SQL Database** accessed with **Dapper**, authentication with **Microsoft Entra ID** and database-driven authorization. Development is incremental (Agile): the specification in [`docs/specs/SPECIFICATIONS.md`](docs/specs/SPECIFICATIONS.md) is delivered sprint by sprint.

**Sprint 1:** `GET /api/v1/demands` returns the signed-in user's identity and only their own demands. Everything else in the specification (SignalR negotiation, PDF upload, the third endpoint) is deferred.

## Repository layout

```text
.
├── Klesia.sln
├── database/Klesia.Database     SQL database project (owns the schema; builds a DACPAC)
├── docs/                        SPECIFICATIONS.md, local-development.md
├── src/
│   ├── Domain                   ApplicationUser, Demand, SirenNumber
│   ├── Application              use cases (DemandService), DTOs, repository abstractions
│   ├── Infrastructure           Dapper repositories, SQL connection, SharePoint link resolver
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
- **.NET 10** (`global.json` pins the SDK).
- **Authorization rule:** only active users present in `sec.User` are served, and only their own demands.
- **PDF links** are SharePoint URLs returned as stored (validated as HTTPS); SharePoint permissions protect the documents.
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

`.claude/agents/ef-core-engineer.md` and `.claude/skills/ef-core-patterns` are generic leftovers and do not apply to this repository.

## License

See [LICENSE](LICENSE).
