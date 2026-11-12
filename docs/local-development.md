# Local development against Azure Database for MySQL

This guide lets you run the KLESIA API on your PC while it talks to an **Azure Database for MySQL – Flexible Server** (MySQL 8.4). No password is stored anywhere: the API and the database migrator sign in with your own Microsoft Entra account and use the resulting **access token as the MySQL password** (token lifetime 5 to 60 minutes, refreshed automatically by the API).

> The exact server settings (name, network access, Entra administrator, managed identity) are still to be confirmed with the customer's DSI, see **OPEN-07** in the specification. Everything below is configuration, never code.

## 1. Prerequisites

- .NET 10 SDK (`dotnet --version` must report 10.x; `global.json` pins it).
- Azure CLI signed in (`az login`) **or** Visual Studio signed in with the same Entra account.
- An Azure Database for MySQL Flexible Server running **MySQL 8.4**, with Microsoft Entra authentication enabled, an Entra administrator, TLS enabled (the default) and a database named `klesia`.
- The server firewall allows your public IP (Azure portal → server → *Networking* → add your client IP), unless the server uses private access.
- Optional, to inspect the database: the `mysql` command-line client or MySQL Workbench. With an Entra token they must enable the **clear-text authentication plugin** (`--enable-cleartext-plugin`), and TLS.

## 2. Give accounts access to the database

Connect once as the **Entra administrator** of the server (user name = the administrator's Entra name, password = an access token):

```powershell
$token = az account get-access-token --resource-type oss-rdbms --output tsv --query accessToken
mysql -h <server>.mysql.database.azure.com --user <entra admin name> --enable-cleartext-plugin --password=$token
```

Then create the database users. `CREATE AADUSER` creates the user but grants **no** permission, so grant what each principal needs:

```sql
-- Developer account: the API only reads (until POST /api/v1/demands ships, then add INSERT on Demand).
CREATE AADUSER 'your.name@talan.com';
GRANT SELECT ON klesia.* TO 'your.name@talan.com'@'%';

-- Deployment principal (person or pipeline running the migrator).
-- DROP and DELETE are deliberately not granted: the migrator only moves forward (DB-11).
CREATE AADUSER 'klesia-deployer@talan.com';
GRANT SELECT, INSERT, UPDATE, CREATE, ALTER, INDEX, REFERENCES ON klesia.* TO 'klesia-deployer@talan.com'@'%';
```

A user name longer than 32 characters needs an alias: `CREATE AADUSER 'long.name@talan.com' AS 'longname';`, and `longname` is then the `User ID`.

The API running in Azure uses the **user-assigned managed identity** (Flexible Server supports no other kind). Its database user is created from the identity's client id:

```sql
SET aad_auth_validate_oids_in_tenant = OFF;
CREATE AADUSER 'klesia-api' IDENTIFIED BY '<client id of the user-assigned managed identity>';
GRANT SELECT ON klesia.* TO 'klesia-api'@'%';
```

> The managed-identity statements and the `GRANT ... @'%'` form follow the general Azure Database for MySQL documentation and have **not** been run against your server. Check them against the current Microsoft documentation (links in the specification, §11) before relying on them.

## 3. Configure the API

Open `src/WebApi/appsettings.Development.json`: it holds placeholders. **Do not commit real values.** Put yours in a new, git-ignored file `src/WebApi/appsettings.Local.json`, which overrides the Development file:

```json
{
  "ConnectionStrings": {
    "KlesiaDb": "Server=<server>.mysql.database.azure.com;Port=3306;Database=klesia;User ID=your.name@talan.com;SslMode=Required;ConnectionTimeout=30"
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
| `ConnectionStrings:KlesiaDb` | MySQL connection **without a `Password`**: when none is present the API acquires an Entra access token (scope `https://ossrdbms-aad.database.windows.net/.default`) and uses it as the password. `User ID` is your Entra database user. |
| `AzureAd:TenantId` | The single Entra tenant allowed to call the API. |
| `AzureAd:ClientId` | Application (client) ID of the **API** app registration (the token audience). |
| `AzureAd:RequiredScope` | Name of the delegated permission exposed by the API (e.g. `access_as_user`), not the full `api://…` URI. |
| `Cors:AllowedOrigins` | Exact frontend origins: `http://localhost:5173` and your deployed Static Web App URL. |

On the Azure container app set the environment variable `AZURE_CLIENT_ID` to the client id of the user-assigned managed identity, so `DefaultAzureCredential` picks it (see [`deployment-container-apps.md`](deployment-container-apps.md)).

In the API app registration set `requestedAccessTokenVersion` to `2` in the manifest, so tokens are issued by `…/v2.0`.

The API refuses to start while a value is empty or still a `<placeholder>`, and says which one.

## 4. Run

```powershell
dotnet run --project src/WebApi --launch-profile https
```

- API: `https://localhost:7180` (HTTP `http://localhost:5180`), health probe `GET /health`, OpenAPI document in Development.
- Call `GET /api/v1/demands` with `Authorization: Bearer <access token for the API>`.

Once the `Dockerfile` exists (S1-OPS-02), `docker build -t klesia-api .` and `docker run -p 8080:8080` with the environment variables above start the same API on `http://localhost:8080`. `GET /health` answers, but database calls fail with `503`: `DefaultAzureCredential` cannot use your `az login` session from inside a container. Keep using `dotnet run` for day-to-day work; the image is verified in CI and in the dev environment.

## 5. Build and apply the database migrations

MySQL has no DACPAC/`SqlPackage`. The schema is created by the **migrator** in `database/Klesia.Database` (DbUp): `Migrations/*.sql` run once and are recorded in the `SchemaVersions` table, `Seeds/*.sql` run on every execution.

```powershell
dotnet build database/Klesia.Database/Klesia.Database.csproj -c Debug

$env:KLESIA_DB_DEPLOY_CONNECTION = "Server=<server>.mysql.database.azure.com;Port=3306;Database=klesia;User ID=klesia-deployer@talan.com;SslMode=Required"
dotnet run --project database/Klesia.Database -c Debug
```

Rules to remember:

- **Never edit a migration that has been applied anywhere.** Add a new, higher-numbered script (`0005_...sql`). Scripts are embedded automatically (`Migrations\*.sql`).
- **One DDL object per script.** MySQL DDL is not transactional: a failed script cannot be rolled back, so a script must not hold several statements that depend on each other.
- No semicolon inside a comment or a string literal (the script splitter cuts on it).
- The seeds are idempotent and insert-only for users. To authorize another identity, append a `UNION ALL SELECT` line with its lower-case Entra `oid` in `Seeds/02_User.sql`.
- There is no automatic data-loss protection: review every destructive change (drop, narrowing, rename) in code review.

## 6. Tests

```powershell
dotnet test Klesia.sln
```

The database constraint tests are skipped unless you point them at a **disposable**, already migrated MySQL 8.4 database:

```powershell
$env:KLESIA_TEST_DB_CONNECTION = "Server=<server>.mysql.database.azure.com;Port=3306;Database=<test-database>;User ID=<test user>;SslMode=Required"
dotnet test tests/IntegrationTests --filter "FullyQualifiedName~DatabaseConstraintTests"
```

Each test runs in a transaction that is rolled back.

## 7. Troubleshooting

| Symptom | Likely cause |
|---|---|
| `Access denied for user ... (using password: YES)` | Entra user not created (`CREATE AADUSER`), wrong `User ID`, expired token, or the principal lacks `GRANT`s. |
| TLS / SSL error | The server requires TLS: keep `SslMode=Required` (or `VerifyFull` once the CA chain is confirmed). |
| Clear-text / authentication plugin error | The driver must send the token unhashed over TLS: keep `SslMode` at `Required` or stricter. |
| `Check constraint '...' is violated` (error 3819) | The row breaks a `CK_*` rule (GK number, HTTPS link, status code, GUID format, 0/1 flag). |
| `Can't connect` / timeout | Your IP is not in the server firewall, or the server is private. |

## 8. Claude Code hooks (`.claude/settings.json`)

| Hook | When | What it does |
|---|---|---|
| `secret-guard.ps1` | Before a file is written or edited | Blocks passwords, keys, SAS signatures, JWTs and client secrets in tracked files (`appsettings.Local.json` is exempt). |
| `format-cs.ps1` | After a `.cs` file is written or edited | Applies the `.editorconfig` whitespace rules to that file. Never blocks. |
| `verify-build.ps1` | When Claude finishes | If code or database files changed: `dotnet build`, `dotnet test`, and the database migrator build. A failure sends Claude back to fix it. |

The hooks run through Windows PowerShell (`powershell`). Review them once with the `/hooks` command in Claude Code.
