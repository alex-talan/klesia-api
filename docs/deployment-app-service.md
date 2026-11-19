# Deploying the KLESIA API to Azure App Service

The API runs on **Azure App Service** (specification **DEC-07**; the Container Apps decision DEC-15 is withdrawn). The normative requirements are in [`SPECIFICATIONS.md`](specs/SPECIFICATIONS.md) section 6.3 (HOST-01 to HOST-11) and, for the documents, DEC-24. This guide explains how they fit together and what is still open.

> **Status.** Nothing here has been run against a real Azure environment. The GitHub Actions workflow and any Bicep do not exist yet. The settings of the customer's environment are open: see **OPEN-08** (App Service), **OPEN-11** and **OPEN-12** (storage) in the specification.

## 1. Topology

```text
GitHub Actions ── publish SQL project (SqlPackage) ──> Azure SQL Database
      │
      └── deploy package ──> App Service "klesia-api" ── Entra token, no password ──> Azure SQL Database
                                   │  user-assigned managed identity
                                   ├── upload / stream to the owner / read back ──> Azure Blob Storage (private container)
                                   ├── analyze PDF ──> Azure Document Intelligence
                                   └── negotiate / demandUpdated ──> Azure SignalR Service

Static Web App (Vue) ── bearer token ──> App Service   (GET .../bae streams the PDF; the browser never reaches the storage)
```

## 2. Platform settings (HOST-01 to HOST-03)

| Setting | Value | Why |
|---|---|---|
| Plan and tier | To confirm (OPEN-08a) | Must support **Always On** |
| Always On | **On** | The extraction runs in the API process after the response (EXT-01); an unloaded app would stop it |
| HTTPS Only | On | HTTPS is mandatory outside localhost |
| Minimum TLS version | 1.2 | |
| Health check path | `/health` | Anonymous, detail-free (spec section 5.3). Because it reports the database, confirm the platform's reaction to a long outage (OPEN-08d) |
| Instances | To confirm (OPEN-08d) | The API is stateless; extractions run in the instance that accepted the request |

## 3. Application settings (HOST-04, HOST-05)

| Setting | Value | Notes |
|---|---|---|
| `AZURE_CLIENT_ID` | Client id of the user-assigned managed identity | Makes `DefaultAzureCredential` select it |
| `AzureAd__TenantId`, `AzureAd__ClientId` | Tenant GUID, API app registration id | OPEN-03 |
| `AzureAd__RequiredScope` | `access_as_user` | DEC-11 |
| `Cors__AllowedOrigins__0` | The deployed Static Web App origin | One setting per origin; never `*` |
| `ConnectionStrings__KlesiaDb` | `Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Default;Encrypt=True` | No password |
| `Storage__BlobServiceUri`, `Storage__ContainerName` | `https://<account>.blob.core.windows.net`, container name | DEC-24 |
| `Upload__MaxPdfSizeMb` | `10` (provisional) | Must equal the webapp's `maxPdfSizeMb`; align the host's request limits (HOST-07) |
| `DocumentIntelligence__Endpoint`, `DocumentIntelligence__ModelId` | From the Document Intelligence owners | OPEN-09 |
| `AzureSignalR__Endpoint` | The SignalR resource endpoint | Spec section 8.1 |

None of these is a secret. Should one ever appear, use a Key Vault reference in the application setting, never the repository.

## 4. Identity and roles (HOST-04)

One user-assigned managed identity, attached to the web app, is used for everything:

| Target | Role or grant |
|---|---|
| Azure SQL Database | Database user created from the identity, with the privileges of spec section 6.2 (see [`local-development.md`](local-development.md) section 2) |
| Blob Storage container | *Storage Blob Data Contributor* (upload, read back, best-effort delete) |
| Document Intelligence | *Cognitive Services User* |
| SignalR Service | The role of spec section 8.1 |

## 5. Storage account and document access (DEC-24, OPEN-12)

- The container is **private**; anonymous blob access is disabled and **shared-key access is disabled** on the account. Only the API, through its managed identity, can read or write.
- `Demand.BaeLink` holds the blob URL without SAS and is never returned. `GET` and `POST /api/v1/demands` return `bae_link` = `/api/v1/demands/{demand_id}/bae`, a stable API path.
- `GET /api/v1/demands/{demand_id}/bae` (spec section 5.4) is the only way to read a PDF. It selects the demand by its id **and** the caller's id in one query (`404` for a missing or foreign id) and **streams** the blob: nothing is buffered whole or written to disk.
- No SAS, no CORS and no public endpoint are needed on the storage, so the account can be restricted to the App Service (firewall or private endpoint, OPEN-12a).
- The PDFs now travel through the App Service instances: check the plan size and the platform request timeout (OPEN-12d, OPEN-08f).
- The frontend must fetch the route with the bearer token and show an object URL; a plain link cannot carry the token.

## 6. Delivery pipeline (HOST-11, GitHub Actions)

Outline for the workflow to be written:

1. `dotnet restore`, `dotnet build`, `dotnet test`, `dotnet publish src/WebApi`.
2. **Publish the SQL project**: `SqlPackage /Action:Publish` of the DACPAC with the deployment principal (`KLESIA_SQL_DEPLOY_CONNECTION`, Entra, no stored password).
3. **Deploy** the package to the web app (to a staging slot first when the plan has slots).
4. **Smoke test**: `GET /health` answers `200` (AT-22), then the dev checks of AT-33.
5. **Rollback**: keep the previous version available (slot swap back or redeploy).

Azure sign-in uses an OpenID Connect federated credential, so no client secret is stored in GitHub (OPEN-08i). The previous version serves during the publish and after a rollback, so every schema change must be backward compatible with it (add first, remove in a later release).

## 7. Not verified yet

- No App Service exists for this project in this repository, no workflow has run, and no storage account or role assignment has been created.
- How the App Service Health check reacts to a database outage, the host's request-size limits and the platform request timeout must be confirmed before go-live (OPEN-08d, OPEN-08f).
- The links in the specification's reference list were written from memory and not opened.

## References

- Health check: https://learn.microsoft.com/azure/app-service/monitor-instances-health-check
- Common settings (Always On, HTTPS Only): https://learn.microsoft.com/azure/app-service/configure-common
- Authorize access to blobs with Microsoft Entra ID: https://learn.microsoft.com/azure/storage/blobs/authorize-access-azure-active-directory
