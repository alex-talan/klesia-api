---
name: azure-container-apps
description: Azure Container Apps hosting rules for Klesia (spec DEC-15, replacing App Service) - multi-stage non-root Dockerfile on port 8080, TLS at the ingress with forwarded headers, startup/readiness/liveness probes, user-assigned managed identity, environment-variable configuration, stateless replicas, GitHub Actions pipeline with the DbUp migrator as a step. Use when writing or reviewing a Dockerfile, deployment pipeline, Bicep, hosting-related startup code, or when anything says App Service.
---

# Azure Container Apps Hosting (Klesia)

The Klesia API is deployed to **Azure Container Apps** (specification **DEC-15**), not to Azure App Service. `docs/specs/SPECIFICATIONS.md` section 6.3 (HOST-01 to HOST-12) is normative and `docs/deployment-container-apps.md` explains it; both always win over this skill. Application architecture, HTTP contract, authorization and the MySQL database are unchanged.

## When to Use This Skill

- Writing or reviewing the `Dockerfile`, `.dockerignore`, a GitHub Actions workflow or Bicep
- Touching startup code that depends on the host: forwarded headers, HTTPS redirection, ports, health checks, logging sinks, shutdown
- Deciding where a setting, a secret or a file lives in the deployed app
- Reviewing a change for "does this still work with several replicas behind an ingress?"
- Replacing any leftover App Service wording (application settings, App Service identity, App Service health check)

Related skills: `azure-integration` (managed identity, typed options), `mysql-database` (Entra token, migrator), `security-audit`, `logging-patterns`.

## Fixed Decisions

| Topic | Rule |
|-------|------|
| Image | Multi-stage `Dockerfile` at the repository root: SDK image publishes `src/WebApi`, ASP.NET Core 10 runtime image runs it. SHA-tagged, never `latest`. No migrator, tests, docs, `.claude/`, `appsettings.Local.json` or secret inside (HOST-01) |
| Port and user | Plain HTTP on `8080` (`ASPNETCORE_HTTP_PORTS=8080`), non-root user, no certificate and no Kestrel TLS (HOST-02) |
| TLS and CORS | TLS ends at the ingress. The ingress CORS policy stays disabled: the API's CORS policy is the only one (HOST-03) |
| Forwarded headers | `UseForwardedHeaders` first in the pipeline, `XForwardedFor` and `XForwardedProto`, known proxies and networks cleared because the ingress is the only entry. Client IP is the rightmost `X-Forwarded-For` value (HOST-04) |
| Probes | Startup and readiness: `GET /health`. Liveness: TCP on 8080. No new routes (HOST-05) |
| Identity | One user-assigned managed identity, selected by the environment variable `AZURE_CLIENT_ID`. No secret, certificate or database password (HOST-06) |
| Configuration | Environment variables in `Section__Key` form. A secret, if one ever exists, is a Container Apps secret or Key Vault reference (HOST-07) |
| State | Stateless: no file on the container disk, no correctness-relevant in-memory state, no sticky session, no data-protection key ring (HOST-08) |
| Logs | Console only, collected in Log Analytics, rules of the logging skill unchanged (HOST-10) |
| Migrator | Not in the image, never at API startup. The pipeline runs it with the deployment principal before the new revision; migrations stay backward compatible with the previous revision (HOST-12) |
| Undecided | OPEN-08 items (environment, network path to MySQL, ingress exposure, registry, replicas, body-size limit) are never hard-coded and dependent work is reported as unverified |

## Forwarded Headers (composition root)

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // GOOD only because the Container Apps ingress is the sole entry point (HOST-04).
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

WebApplication app = builder.Build();
app.UseForwardedHeaders(); // before HTTPS redirection, authentication, CORS and logging
```

`TreatWarningsAsErrors` is on in this repository. If `KnownNetworks` is flagged obsolete on the pinned .NET 10 SDK (a replacement named `KnownIPNetworks` exists in newer ASP.NET Core), use the name that compiles without a warning.

## Dockerfile Rules

- Copy `global.json`, `Directory.Build.props`, `Directory.Packages.props` and `src/` only, then `dotnet publish src/WebApi/WebApi.csproj -c Release`. Do not restore the whole solution (tests and the migrator are not needed).
- Final stage: runtime image, `ENV ASPNETCORE_HTTP_PORTS=8080`, `USER $APP_UID`, `EXPOSE 8080`.
- No `ENV` or `ARG` carrying a tenant id, client id, connection string or token. Settings arrive at runtime.
- `.dockerignore` excludes `**/bin`, `**/obj`, `.git`, `.claude`, `docs`, `tests`, `database` and `**/appsettings.Local.json`.
- Verify the base image tags and `APP_UID` against the current Microsoft images before relying on them.

## Pipeline Rules (GitHub Actions)

1. Restore, build, test.
2. Build the image, tag it with the commit SHA, push it to the registry.
3. Run the migrator (`database/Klesia.Database`) with `KLESIA_DB_DEPLOY_CONNECTION` and the deployment principal.
4. Deploy a new revision with the SHA-tagged image.
5. Smoke test `GET /health`, keep the previous revision for rollback.

Azure sign-in uses an OpenID Connect federated credential, never a stored client secret. The runtime identity keeps `SELECT` only.

## Common Mistakes

| Mistake | Why it is wrong here |
|---------|----------------------|
| `UseHttps` / a certificate in the container | TLS ends at the ingress; Kestrel serves plain HTTP on 8080 |
| Forwarded headers registered after authentication or redirection | The scheme and client IP are wrong, redirect loops appear |
| Liveness probe on `/health` | A MySQL outage restarts every replica and does not fix anything |
| Ingress CORS enabled together with the API's CORS | Two policies that can disagree |
| Writing uploads or caches to local disk | Replicas do not share a disk; the PDF goes to SharePoint (DEC-13) |
| Running the migrator at API startup | The runtime identity has `SELECT` only; schema changes belong to the pipeline |
| A migration that drops or renames a column in the same release | The previous revision still serves during the rollout and after a rollback |
| App Service wording or settings (`WEBSITE_*`, application settings, App Service health check) | Replaced by DEC-15 |
| `latest` tag or a secret baked into the image | Not reproducible, not safe |

## Review Checklist

### 1. Image
- [ ] Multi-stage, non-root, port 8080, no TLS inside, no secret or `appsettings.Local.json`
- [ ] SHA tag, `.dockerignore` present

### 2. Runtime behavior
- [ ] `UseForwardedHeaders` first with `XForwardedFor` and `XForwardedProto`
- [ ] `/health` unchanged, no probe routes added, liveness is TCP
- [ ] Console logging only, stateless, no local files

### 3. Identity and configuration
- [ ] `AZURE_CLIENT_ID` selects the user-assigned managed identity
- [ ] Settings are `Section__Key` environment variables, no secret in the repository or image

### 4. Delivery
- [ ] Migrator runs as a pipeline step before the new revision, with the deployment principal
- [ ] Migrations are backward compatible with the previous revision
- [ ] Anything depending on OPEN-08 is reported as unverified; no deployment is claimed verified without having been run

## Token Optimization

```bash
rg -n "UseForwardedHeaders|ForwardedHeadersOptions|UseHttps|UseHsts|UseHttpsRedirection" src
rg -n "App Service|AppService|WEBSITE_|application setting" . --glob '!obj' --glob '!bin'
rg -n "AZURE_CLIENT_ID|ASPNETCORE_HTTP_PORTS" .
```
