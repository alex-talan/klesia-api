# Local development against Azure SQL Database

This guide lets you run the KLESIA API on your PC while it talks to an **Azure SQL Database**. No password is stored anywhere: the connection uses `Authentication=Active Directory Default`, which signs in with your own Microsoft Entra account.

## 1. Prerequisites

- .NET 10 SDK (`dotnet --version` must report 10.x; `global.json` pins it).
- Azure CLI signed in (`az login`) **or** Visual Studio signed in with the same Entra account.
- `SqlPackage` CLI (for publishing the database): `dotnet tool install -g microsoft.sqlpackage`.
- An Azure SQL Database whose server firewall allows your public IP (Azure portal → server → *Networking* → add your client IP).

## 2. Give your account access to the database

Run once, connected to the target database as an Entra admin of the server:

```sql
CREATE USER [your.name@talan.com] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [your.name@talan.com];
```

The API only reads. (Publishing the schema needs a more privileged principal, see §5.)

## 3. Configure the API

Open `src/WebApi/appsettings.Development.json`: it holds placeholders. **Do not commit real values.** Put yours in a new, git-ignored file `src/WebApi/appsettings.Local.json`, which overrides the Development file:

```json
{
  "ConnectionStrings": {
    "KlesiaDb": "Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;ConnectRetryCount=3;ConnectRetryInterval=10"
  },
  "AzureAd": {
    "TenantId": "<tenant guid>",
    "ClientId": "<API app registration (client) id>",
    "RequiredScope": "<delegated scope name, e.g. access_as_user>"
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5173" ]
  }
}
```

Equivalent environment variables: `ConnectionStrings__KlesiaDb`, `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__RequiredScope`, `Cors__AllowedOrigins__0`.

| Setting | Meaning |
|---|---|
| `AzureAd:TenantId` | The single Entra tenant allowed to call the API. |
| `AzureAd:ClientId` | Application (client) ID of the **API** app registration (the token audience). |
| `AzureAd:RequiredScope` | Name of the delegated permission exposed by the API (e.g. `access_as_user`), not the full `api://…` URI. |
| `Cors:AllowedOrigins` | Exact frontend origins: `http://localhost:5173` and your deployed Static Web App URL. |

In the API app registration set `requestedAccessTokenVersion` to `2` in the manifest, so tokens are issued by `…/v2.0`.

The API refuses to start while a value is empty or still a `<placeholder>`, and says which one.

## 4. Run

```powershell
dotnet run --project src/WebApi --launch-profile https
```

- API: `https://localhost:7180` (HTTP `http://localhost:5180`), health probe `GET /health`, OpenAPI document in Development.
- Call `GET /api/v1/demands` with `Authorization: Bearer <access token for the API>`.

## 5. Build and publish the database

```powershell
dotnet build database/Klesia.Database/Klesia.Database.sqlproj -c Debug

$env:KLESIA_SQL_DEPLOY_CONNECTION = "Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Default;Encrypt=True"
SqlPackage /Action:Publish `
  /SourceFile:"database/Klesia.Database/bin/Debug/Klesia.Database.dacpac" `
  /Profile:"database/Klesia.Database/Properties/Azure.publish.xml" `
  /TargetConnectionString:$env:KLESIA_SQL_DEPLOY_CONNECTION
```

The deploying account needs rights to create objects. The publish profile blocks data loss and never drops objects missing from the source. The post-deployment scripts are idempotent: they add the reference data and the seeded `admin` user (edit `Scripts/02_User.sql` to add more authorized identities, using their Entra `oid`).

## 6. Tests

```powershell
dotnet test Klesia.sln
```

The SQL constraint tests are skipped unless you point them at a **disposable** published database:

```powershell
$env:KLESIA_TEST_SQL_CONNECTION = "Server=tcp:<server>.database.windows.net,1433;Database=<test-database>;Authentication=Active Directory Default;Encrypt=True"
dotnet test tests/IntegrationTests --filter "FullyQualifiedName~SqlConstraintTests"
```

Each test runs in a transaction that is rolled back.

## 7. Claude Code hooks (`.claude/settings.json`)

| Hook | When | What it does |
|---|---|---|
| `secret-guard.ps1` | Before a file is written or edited | Blocks passwords, keys, SAS signatures, JWTs and client secrets in tracked files (`appsettings.Local.json` is exempt). |
| `format-cs.ps1` | After a `.cs` file is written or edited | Applies the `.editorconfig` whitespace rules to that file. Never blocks. |
| `verify-build.ps1` | When Claude finishes | If code or database files changed: `dotnet build`, `dotnet test`, and the SQL project build. A failure sends Claude back to fix it. |

The hooks run through Windows PowerShell (`powershell`). Review them once with the `/hooks` command in Claude Code.
