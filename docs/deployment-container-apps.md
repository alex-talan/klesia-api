# Deploying the KLESIA API to Azure Container Apps

The customer's DSI requires that the API run in **Azure Container Apps**, not Azure App Service (specification **DEC-15**, which supersedes DEC-07). The normative requirements are in [`SPECIFICATIONS.md`](specs/SPECIFICATIONS.md) section 6.3 (HOST-01 to HOST-12); this guide explains how they fit together and what remains to decide.

> **Status.** Nothing here has been run against a real Azure environment. The `Dockerfile`, the GitHub Actions workflow and any Bicep do not exist yet (SC-OPS-02, still to be written). The settings of the customer's environment are still open: see **OPEN-08** in the specification.

## 1. Target topology

```text
GitHub Actions ──push image──> Azure Container Registry
      │                                │ pull (AcrPull, managed identity)
      │ SqlPackage publish (deployer)  ▼
      ▼                      Container Apps environment
Azure SQL Database       <── Container App "klesia-api" (HTTP ingress, port 8080)
   (Entra, no password)              ▲ HTTPS (TLS ends at the ingress)
                                     │
                        Static Web App (Vue frontend) ── sign-in ──> Microsoft Entra ID
```

## 2. What changes compared with App Service

| Concern | App Service (old) | Container Apps (now) |
|---|---|---|
| Deployment unit | Zip / publish artifact | Container image built from the root `Dockerfile` |
| Port and TLS | Platform-managed | Plain HTTP on `8080` in the container, TLS ends at the ingress |
| Client IP and scheme | Platform headers | `X-Forwarded-For` / `X-Forwarded-Proto`, read by `UseForwardedHeaders` (HOST-04) |
| Health | App Service health check on `/health` | Startup and readiness probes on `/health`, TCP liveness probe (HOST-05) |
| Settings | Application settings | Environment variables of the container app (`Section__Key`) |
| Managed identity | App Service identity, `AZURE_CLIENT_ID` app setting | User-assigned identity attached to the container app, `AZURE_CLIENT_ID` environment variable |
| Logs | Platform log streams | Console output collected in Log Analytics |
| Scale | Plan size | Replicas with a scaling rule, so the API must stay stateless (HOST-08, HOST-09) |
| Database schema | Unchanged | Unchanged: SQL database project (DACPAC), published by the pipeline with `SqlPackage` (HOST-12) |
| Request size and time | App Service limits | Ingress limits, 240 s request timeout, body limit to confirm (OPEN-08f) |

Nothing changes in the code architecture, the HTTP contract, authorization or the database.

## 3. Container image (HOST-01, HOST-02)

Requirements: multi-stage, non-root, plain HTTP on `8080`, no secret or tenant value, no SQL project, no tests, no `appsettings.Local.json`, SHA-tagged.

Reference sketch, to be validated when the image is built (image tags and the `APP_UID` variable must be checked against the current Microsoft images):

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/WebApi/WebApi.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebApi.dll"]
```

`.dockerignore` must exclude at least: `**/bin`, `**/obj`, `.git`, `.claude`, `docs`, `tests`, `database`, `**/appsettings.Local.json`.

## 4. Settings of the container app (HOST-07)

| Environment variable | Value | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Keeps the OpenAPI document off (anonymous routes are `/health` only) |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Matches the ingress target port |
| `AZURE_CLIENT_ID` | Client id of the user-assigned managed identity | Makes `DefaultAzureCredential` select it (DEC-06) |
| `AzureAd__TenantId` | Entra tenant GUID | OPEN-03 |
| `AzureAd__ClientId` | API app registration (client) id | OPEN-03 |
| `AzureAd__RequiredScope` | `access_as_user` | DEC-11 |
| `Cors__AllowedOrigins__0` | The deployed Static Web App origin | One variable per origin (`__1`, ...). Never `*` |
| `ConnectionStrings__KlesiaDb` | `Server=tcp:<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30` | No `Password`; OPEN-07 |

None of these is a secret. If a secret ever appears, use a Container Apps secret (or a Key Vault reference through the managed identity), never the image or the repository.

## 5. Probes (HOST-05)

| Probe | Type | Target | Why |
|---|---|---|---|
| Startup | HTTP GET | `/health` on 8080 | The first call acquires the Entra token and opens the TLS connection to Azure SQL Database |
| Readiness | HTTP GET | `/health` on 8080 | `503` (database unreachable) takes the replica out of rotation |
| Liveness | TCP | port 8080 | A database outage must not restart replicas |

Thresholds, period and timeout are infrastructure settings (OPEN-08e).

## 6. Identity and database users (HOST-06)

1. Create one user-assigned managed identity and attach it to the container app.
2. Set `AZURE_CLIENT_ID` to its client id.
3. Create its Azure SQL database user and grant the runtime privileges (`db_datareader`, `INSERT` and the column-level `UPDATE`, spec section 6.2) as described in [`local-development.md`](local-development.md) section 2 (`CREATE USER [<identity name>] FROM EXTERNAL PROVIDER`). The Entra administrator and server identity prerequisites are in OPEN-07.
4. If a registry is used, give the identity `AcrPull` on it (OPEN-08d).
5. Grant the identity the SignalR role (spec section 8.1, DEC-16), `Sites.Selected` on the SharePoint site (DEC-13) and *Cognitive Services User* on the Document Intelligence resource (DEC-18).

## 7. Delivery pipeline (HOST-12, GitHub Actions)

Outline for the workflow still to be written:

1. **Build and test:** `dotnet restore`, `dotnet build`, `dotnet test` (database constraint tests only when a disposable Azure SQL database is provided).
2. **Image:** `docker build`, tag with the commit SHA, push to the registry.
3. **Publish the database:** `dotnet build database/Klesia.Database/Klesia.Database.sqlproj`, then `SqlPackage /Action:Publish` of the DACPAC with `KLESIA_SQL_DEPLOY_CONNECTION` and the **deployment** principal (not the runtime identity, which has no DDL privilege).
4. **Deploy:** create a new revision of the container app with the SHA-tagged image.
5. **Smoke test:** `GET /health` returns `200` (AT-22), then the dev checks of AT-36.
6. **Rollback:** keep the previous revision available; route traffic back to it if the smoke test fails.

Azure sign-in uses an OpenID Connect federated credential on the deployment principal, so no client secret is stored in GitHub (OPEN-08i). Use GitHub environments for approvals.

**Schema changes and rollbacks.** The `SqlPackage` publish runs before the new revision takes traffic, and a rollback leaves the old API on the new schema. Every schema change must therefore be backward compatible with the previous API version: add columns and tables first, remove or rename them in a later release (DB-11).

## 8. Open items for the DSI (OPEN-08)

- Container Apps environment, region and virtual network integration.
- Network path to Azure SQL Database: public access with firewall rules is not workable with unstable outbound addresses, so private access is preferred.
- Ingress: external or internal, IP restrictions, custom domain and certificate.
- Container registry, authentication and retention.
- Minimum and maximum replicas (at least 1 because of the background extraction, OPEN-10), CPU and memory, scaling rule, probe thresholds.
- Ingress body-size limit against `Upload:MaxPdfSizeMb` (before production).
- Who owns the infrastructure code (Bicep) and the role assignments.
- Log Analytics workspace and telemetry backend.
- GitHub to Azure authentication and approvals.

## 9. Not verified yet

- The default values of the Container Apps probes, the environment variables Container Apps exposes for managed identity, and the ingress request-body limit were not confirmed against Microsoft's documentation. Confirm them before go-live.
- No image has been built, no revision deployed, and the SQL project has not been published from a pipeline.

## References

- Ingress: https://learn.microsoft.com/azure/container-apps/ingress-overview
- ASP.NET Core on Container Apps: https://learn.microsoft.com/azure/container-apps/dotnet-overview
- Health probes: https://learn.microsoft.com/azure/container-apps/health-probes
- Managed identity: https://learn.microsoft.com/azure/container-apps/managed-identity
- Forwarded headers: https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer
