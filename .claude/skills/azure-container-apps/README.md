# Azure Container Apps

Human-facing guide for the `azure-container-apps` skill.

## Purpose

The customer's DSI requires the KLESIA API to run in **Azure Container Apps**, not Azure App Service (specification DEC-15). This skill keeps the Dockerfile, the pipeline and the host-dependent startup code aligned with spec section 6.3 (HOST-01 to HOST-12).

## When To Use It

- Writing or reviewing the `Dockerfile`, `.dockerignore`, GitHub Actions workflow or Bicep
- Configuring forwarded headers, ports, probes, logging or shutdown
- Deciding where a setting, secret or file lives in the deployed app
- Removing leftover App Service wording

## Quick Reference

| Area | Rule |
|------|------|
| Image | Multi-stage, non-root, plain HTTP on 8080, SHA tag, no secret |
| TLS | Ends at the ingress; `UseForwardedHeaders` first |
| Probes | `/health` for startup and readiness, TCP for liveness |
| Identity | User-assigned managed identity + `AZURE_CLIENT_ID` |
| Config | `Section__Key` environment variables |
| State | Stateless replicas, console logs only |
| Migrator | Pipeline step before the new revision, never in the image |

## Typical Prompt

```text
Use the azure-container-apps skill to review the Dockerfile and the deployment workflow against spec section 6.3.
```

See also `docs/deployment-container-apps.md`.
