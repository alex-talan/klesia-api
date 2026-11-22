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

-- Runtime privileges of the API beyond reading (spec section 6.2), no DDL:
GRANT INSERT ON dbo.Demand TO [your.name@talan.com];
GRANT INSERT ON dbo.DemandInformation TO [your.name@talan.com];
GRANT INSERT ON dbo.DemandFormula TO [your.name@talan.com];
GRANT UPDATE ON dbo.Demand (StatusId) TO [your.name@talan.com];
```

The API reads everything, inserts a demand and its extraction results, and updates only the demand status. (Publishing the schema needs a more privileged principal, see §5.)

The API running in Azure uses the **user-assigned managed identity** of the App Service. Create its database user from the identity's name and give it the same privileges:

```sql
CREATE USER [<managed identity name>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<managed identity name>];
GRANT INSERT ON dbo.Demand TO [<managed identity name>];
GRANT INSERT ON dbo.DemandInformation TO [<managed identity name>];
GRANT INSERT ON dbo.DemandFormula TO [<managed identity name>];
GRANT UPDATE ON dbo.Demand (StatusId) TO [<managed identity name>];
```

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
    "RequiredScope": "access_as_user"
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5173" ]
  },
  "Storage": {
    "BlobServiceUri": "https://<account>.blob.core.windows.net",
    "ContainerName": "<private container>"
  },
  "Upload": {
    "MaxPdfSizeMb": 10
  },
  "DocumentIntelligence": {
    "Endpoint": "https://<resource>.cognitiveservices.azure.com/",
    "ModelId": "<BAE model id>",
    "TimeoutSeconds": 120
  },
  "Extraction": {
    "MaxConcurrency": 2,
    "QueueCapacity": 100,
    "StaleAfterMinutes": 15,
    "SweepIntervalMinutes": 5
  },
  "AzureSignalR": {
    "Endpoint": "https://<resource>.service.signalr.net",
    "HubName": "demands"
  }
}
```

`AzureAd:Instance` defaults to `https://login.microsoftonline.com/`; `AzureAd:RequiredScope` is fixed at `access_as_user`. `Upload:MaxPdfSizeMb` is provisionally `10` and must match the webapp's runtime `maxPdfSizeMb`. Document Intelligence settings become mandatory when the extraction adapter is wired (T-065); their keys are shown here for the complete local configuration and must remain placeholders until real values are supplied.

Equivalent environment variables include `ConnectionStrings__KlesiaDb`, `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__RequiredScope`, `Cors__AllowedOrigins__0`, `Storage__BlobServiceUri`, `Storage__ContainerName`, `Upload__MaxPdfSizeMb`, `DocumentIntelligence__Endpoint`, `DocumentIntelligence__ModelId`, `DocumentIntelligence__TimeoutSeconds`, `Extraction__MaxConcurrency`, `Extraction__QueueCapacity`, `Extraction__StaleAfterMinutes`, `Extraction__SweepIntervalMinutes`, `AzureSignalR__Endpoint`, `AzureSignalR__HubName`.

| Setting | Meaning |
|---|---|
| `AzureAd:Instance` | Entra authority base URL; defaults to `https://login.microsoftonline.com/`. |
| `AzureAd:TenantId` | The single Entra tenant allowed to call the API. |
| `AzureAd:ClientId` | Application (client) ID of the **API** app registration (the token audience). |
| `AzureAd:RequiredScope` | Name of the delegated permission exposed by the API (e.g. `access_as_user`), not the full `api://…` URI. |
| `Cors:AllowedOrigins` | Exact frontend origins: `http://localhost:5173` and your deployed Static Web App URL. |
| `Storage:BlobServiceUri` | Blob endpoint of the storage account holding the PDFs, `https://<account>.blob.core.windows.net`. |
| `Storage:ContainerName` | Name of the private container for the PDFs. |
| `Upload:MaxPdfSizeMb` | Provisional upload limit, `10` MB; must equal the webapp's `maxPdfSizeMb` and fit the request limits. |
| `DocumentIntelligence:Endpoint`, `DocumentIntelligence:ModelId` | Endpoint and custom BAE model id supplied by the Document Intelligence owners; no key is used. These become mandatory when extraction is wired (T-065). |
| `DocumentIntelligence:TimeoutSeconds` | Analysis timeout; defaults to `120` seconds. |
| `Extraction:MaxConcurrency`, `Extraction:QueueCapacity` | Background worker concurrency and queue capacity; defaults to `2` and `100`. |
| `Extraction:StaleAfterMinutes`, `Extraction:SweepIntervalMinutes` | Recovery sweep age and interval; defaults to `15` and `5` minutes. Stale age must exceed the Document Intelligence timeout. |
| `AzureSignalR:Endpoint` | Azure SignalR resource endpoint; access uses the configured managed identity (no access key). |
| `AzureSignalR:HubName` | Hub identifier; fixed to `demands`. |

On the Azure App Service set the application setting `AZURE_CLIENT_ID` to the client id of the user-assigned managed identity, so `DefaultAzureCredential` picks it (see [`deployment-app-service.md`](deployment-app-service.md)).

### Storage account for the PDFs (DEC-24)

The API stores the PDFs in a **private** Blob container and streams them to their owner itself, so it only needs your Entra account (locally) or the managed identity (in Azure), never an account key or a SAS:

1. Use a development storage account with a private container (no anonymous access) and, if the policy allows it, shared-key access disabled.
2. Grant your account *Storage Blob Data Contributor* on the container (upload, stream, read back, delete):

```powershell
az role assignment create --assignee <your Entra object id> --role "Storage Blob Data Contributor" --scope <container resource id>
```

3. Put `Storage__BlobServiceUri` and `Storage__ContainerName` in `appsettings.Local.json` or the environment. Never put an account key, a connection string or a SAS in a tracked file (`secret-guard.ps1` blocks `sig=` signatures).

> The role name and command are standard Azure ones but were **not** run against your subscription. Role assignments can take several minutes to apply.

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
