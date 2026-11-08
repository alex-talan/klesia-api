# KLESIA — Backend Functional & Technical Specification

**Document ID:** KLESIA-BE-SPEC  
**Version:** 1.1 (Sprint 1 baseline, revised)  
**Date:** 2026-11-08  
**Status:** Implementation-ready for Sprint 1; future contracts recorded separately  
**Language:** English  
**Primary audience:** Backend engineers, database engineers, AI coding agents, QA and infrastructure engineers

> **Purpose.** This document specifies the incremental implementation of the KLESIA backend. Sprint 1 delivers an authenticated, authorized read-only demand-list API and the relational database supporting that view. The future SignalR and document-related contracts are retained here for compatibility **but are not Sprint 1 implementation work**. Later sprints should extend this document rather than silently changing existing contracts.

## 1. Normative language and scope

- **MUST / SHALL**: mandatory for the applicable sprint.
- **SHOULD**: recommended unless there is a documented reason not to follow it.
- **MAY**: optional.
- An explicit **ASSUMPTION** is a proposed implementation choice, not an established client requirement. Agents may use it for Sprint 1 unless an existing repository convention or signed-off decision contradicts it.
- **DEFERRED** requirements are documented interfaces for subsequent sprints; do not create placeholder endpoints that claim to implement them.
- Changes to endpoint semantics, JSON field names, auth requirements, the SQL schema, or access rules MUST be deliberate and documented.

### 1.1 Sprint 1 deliverables (IN SCOPE)

| ID | Required outcome |
|---|---|
| S1-DB-01 | A buildable SQL database project under the already initialized `database/` directory with exactly the four required business/security tables. |
| S1-DB-02 | Database artifacts that build into `database/Klesia.Database/bin/Debug/Klesia.Database.dacpac` and can be published to Azure SQL Database through `SqlPackage`. |
| S1-DB-03 | Working post-deployment reference-data scripts incorporating the existing `01_RefData.sql` and `02_User.sql`, made safe for repeated deployment. |
| S1-AUTH-01 | Entra ID JWT bearer access-token validation for the backend API. |
| S1-AUTH-02 | Database allowlist authorization: only existing, active users can access the protected data. |
| S1-API-01 | `GET /api/v1/demands`, returning the signed-in user's identity details and only their demands. |
| S1-API-02 | Explicit, safe HTTP responses for authentication, authorization, database and other server errors. |
| S1-OPS-01 | Configuration, allowed frontend origins, reproducible local run/build instructions and verification tests. |

### 1.2 Explicitly OUT OF SCOPE for Sprint 1

- `POST /api/v1/notifications/negotiate` and the Azure SignalR `demands` notification channel: **contract defined in §7, implementation deferred**.
- `demandUpdated` publication and demand-status mutation: deferred; no status-changing HTTP endpoint is specified.
- PDF upload, size-validation, ingestion or storage workflows: deferred; **no upload route is defined**.
- Demand creation, update, delete, status transitions, pagination, filtering or searching.
- Provisioning new users via API; management of roles/users via API.
- Additional business entities, audit-history tables, or user-role join tables.
- The third HTTP endpoint of the overall API: planned for a later sprint, contract not yet specified; see **OPEN-01**.

### 1.3 Known gaps, decisions and conflict resolution

| ID | Observation / decision | Handling |
|---|---|---|
| OPEN-01 | The overall API is planned with **three HTTP endpoints**. Sprint 1 delivers `GET /api/v1/demands`; `POST /api/v1/notifications/negotiate` is specified in §7 (deferred); the **third endpoint** is planned for a later sprint and its contract is not yet specified. The `demandUpdated` event is **not** HTTP. | **Scheduled, not missing.** Do not create a third route or a placeholder in Sprint 1. When its contract is supplied, add it in a dedicated sprint section of this document. |
| OPEN-02 | The broad API description includes SignalR, while the final Sprint 1 statement limits functionality to the initial user/demand screen. | The final Sprint 1 restriction takes precedence. Implement the list route now; preserve SignalR as a future contract. |
| OPEN-03 | The precise delegated API scope name, Entra tenant ID and API application ID are absent. | Supplied through configuration (§6.1: `AzureAd:*`); never hard-coded. The API refuses to start while they are missing or still placeholders. |
| OPEN-04 | **Resolved.** The only authorization rule for now is *own data only*: an authenticated, allowlisted, active user receives exclusively the demands whose `Demand.UserId` is their own `sec.User.UserId`. | The role codes `admin`, `operational` and `guest` are confirmed as the real codes, but **no role grants or removes any access in Sprint 1**. Do not infer cross-user, administrator or role-based behavior. |
| OPEN-05 | **Resolved.** The PDFs are stored in **SharePoint**. `Demand.BaeLink` holds the absolute HTTPS URL of the document in SharePoint. | The API returns that URL after validating that it is an absolute HTTPS URL. SharePoint permissions are the access gate: the browser opens the link with the user's own Entra single sign-on session, so no bearer header, no SAS-style signature and no anonymous link is involved. Generating time-limited sharing links through Microsoft Graph is a possible later evolution behind the `IBaeLinkResolver` abstraction; it needs a separate decision (tenant sharing policy, Graph permissions). |
| INT-05 | `maxPdfSizeMb`: 10 MB is illustrative, not confirmed. | Deferred until an upload endpoint is specified. |
| INT-08 | Exact error-body JSON structure is unconfirmed. | Keep status codes stable; return safe, generic `ProblemDetails` bodies, without relying on a frontend-specific error schema. |
| DEC-01 | Backend target framework. | **.NET 10 (`net10.0`)** from the start (SDK pinned in `global.json`). |
| DEC-02 | Data access technology. | **Dapper** over `Microsoft.Data.SqlClient`. **Entity Framework Core, `DbContext` and EF migrations MUST NOT be used.** The SQL database project is the single owner of the schema. |
| DEC-03 | Existing repository files take precedence over earlier draft DDL. | Primary keys are `{Table}Id`, constraint names follow the single scheme of DB-06, column sizes and defaults follow the files already present in `database/`, and the `ref` schema is kept. The draft DDL in earlier revisions of this document is superseded by §3.4. |
| DEC-04 | Where configuration values live. | Entra, CORS and SQL settings are configuration-file values (§6.1), never code constants. |
| DEC-05 | Seeded identity. | The seed script creates the project owner's account as an active `admin` user, as explicitly requested. Further identities are added by editing the same script. |
| DEC-06 | Local development against Azure SQL Database. | One connection string works on a developer PC and in Azure: `Authentication=Active Directory Default` (see §6.1). No password is ever stored. |

## 2. System context and architectural boundaries

```mermaid
flowchart LR
    U[User] --> FE[Vue frontend / Azure Static Web App]
    FE -->|Microsoft Entra sign-in| ENTRA[Microsoft Entra ID]
    FE -->|GET /api/v1/demands\nBearer access token| API[Backend API / ASP.NET Core .NET 10]
    API -->|Validate JWT issuer, audience,\nsignature, lifetime, delegated scope| ENTRA
    API -->|Dapper: lookup active user by Entra oid\nRead owning user's demands| SQL[(Azure SQL Database)]
    FE -->|Opens bae_link with the user's SSO session| SP[SharePoint documents]
    API -.->|Later sprint: negotiate + targeted events| SR[Azure SignalR Service]
```

**Technology baseline:**

- Database: Microsoft SQL Server / Azure SQL Database, modeled through a `.sqlproj` SQL database project (`Microsoft.Build.Sql`). **The SQL project is the only owner of the schema.**
- Backend (**DEC-01**): ASP.NET Core Web API in C# on **.NET 10 (`net10.0`)**, following the existing Clean Architecture solution (`Domain`, `Application`, `Infrastructure`, `WebApi`; dependencies point inward only).
- Data access (**DEC-02**): **Dapper** with `Microsoft.Data.SqlClient`, implemented in `Infrastructure` behind repository interfaces declared in `Application`. **Entity Framework Core, `DbContext`, EF migrations and EF design-time tooling MUST NOT be used.** Every SQL statement is a parameterized, hand-written query.
- Authentication: Microsoft Entra ID, JWT bearer **access tokens intended for this API**, validated with the ASP.NET Core JWT bearer middleware.
- Authorization: explicit server-side SQL allowlist (`sec.User`) after JWT validation; the only access rule is *own demands only* (OPEN-04).
- Documents: PDFs live in SharePoint; `Demand.BaeLink` stores the document URL (OPEN-05).
- Frontend: existing Vue application hosted in Azure Static Web Apps, with Vite local development at `http://localhost:5173`.
- Configuration (**DEC-04**): environment-specific; tenant ID, API client ID, delegated scope, allowed origins and the SQL connection string are configuration values, not source code (§6.1).
- Database connection: least-privilege runtime identity; Azure-managed identity in Azure and the developer's own Entra account on a PC, both through `Authentication=Active Directory Default`. The SQL credential used by `SqlPackage` is a **deployment** concern and must not be copied into frontend settings or application settings.

## 3. Database specification — Sprint 1

### 3.1 Design requirements

| ID | Requirement |
|---|---|
| DB-01 | Use separate files for SQL schemas and every table definition. |
| DB-02 | Exactly four domain tables: `sec.Role`, `sec.User`, `ref.Status`, `dbo.Demand`. Schema usage: `sec` for security tables, `ref` for reference/lookup tables (the status lookup), `dbo` for business data. |
| DB-03 | Every table has a primary key named after its table: `{Table}Id INT IDENTITY(1,1)` — `RoleId`, `UserId`, `StatusId`, `DemandId`. |
| DB-04 | All schema, table and column identifiers use PascalCase, except the schema identifiers `sec`, `dbo` and `ref`. |
| DB-05 | Declare every foreign key explicitly and create an index with the foreign-key column as leading key. Do not create an index that duplicates the primary key or a unique constraint. |
| DB-06 | Name every constraint and index with the **single scheme** below. |
| DB-07 | Enforce all described mandatory fields through `NOT NULL`; apply only justified checks/uniqueness. |
| DB-08 | Use SQL Server-native types. Do not introduce `FLOAT` for future monetary or percentage data. No such fields exist in Sprint 1. |
| DB-09 | Use `NO ACTION` behavior for deletes; do not add cascade deletes. |
| DB-10 | Maintain third-normal-form style associations; do not copy role, status or user names onto `Demand`. |
| DB-11 | A re-publish MUST preserve user-generated and existing business data; destructive schema changes require explicit review. |
| DB-12 | The runtime reads the database exclusively through Dapper with parameterized SQL (see §2). No ORM mapping drives the schema. |

**Naming scheme (DB-06)** — `{Table}` is the unqualified table name; the pattern is the same for every table:

| Object | Pattern | Examples |
|---|---|---|
| Primary key | `PK_{Table}` | `PK_Role`, `PK_Demand` |
| Foreign key | `FK_{Table}_{ReferencedTable}` (the table that holds the column first) | `FK_User_Role`, `FK_Demand_User`, `FK_Demand_Status` |
| Unique (alternate) key | `AK_{Table}_{Column}` | `AK_Role_Code`, `AK_User_UserUniqueId`, `AK_Status_Code` |
| Check | `CK_{Table}_{Column}` (or `{Rule}` when it spans columns) | `CK_Status_Code`, `CK_Demand_SirenNumber`, `CK_Demand_BaeLink` |
| Default | `DF_{Table}_{Column}` | `DF_User_RoleId`, `DF_User_IsActive` |
| Non-unique index | `IX_{Table}_{Column}[_{Column}]` | `IX_User_RoleId`, `IX_Demand_UserId`, `IX_Demand_StatusId` |

#### 3.2 Schema and cardinality

```mermaid
erDiagram
    Role ||--o{ User : "RoleId"
    User ||--o{ Demand : "UserId"
    Status ||--o{ Demand : "StatusId"
    Role {
      int RoleId PK
      nvarchar Name
      nvarchar Code UK
    }
    User {
      int UserId PK
      uniqueidentifier UserUniqueId UK
      int RoleId FK
      nvarchar Name
      nvarchar Email
      bit IsInternal
      bit IsActive
    }
    Status {
      int StatusId PK
      nvarchar Name
      nvarchar DisplayName
      nvarchar Code UK
      nvarchar Description
    }
    Demand {
      int DemandId PK
      int UserId FK
      char SirenNumber
      nvarchar BaeLink
      int StatusId FK
    }
```

**Critical identity distinction:** `sec.User.UserId` is the internal SQL identity (`INT`). `sec.User.UserUniqueId` is the Microsoft Entra user's `oid` (`UNIQUEIDENTIFIER`). The API's `user_id` response field refers to **`UserUniqueId`**, never the internal `UserId`.

### 3.3 Canonical table contracts

#### `sec.Role`

| Column | SQL type | Null | Rules |
|---|---|---|---|
| `RoleId` | `INT IDENTITY(1,1)` | No | `PK_Role` |
| `Name` | `NVARCHAR(300)` | No | Display/human-readable role name |
| `Code` | `NVARCHAR(50)` | No | Unique stable role identifier: `AK_Role_Code`. Confirmed codes: `admin`, `operational`, `guest` |

A role can be associated with multiple users; each user has exactly one role. **No role grants any permission in Sprint 1** (OPEN-04).

#### `sec.User`

| Column | SQL type | Null | Rules |
|---|---|---|---|
| `UserId` | `INT IDENTITY(1,1)` | No | `PK_User` |
| `UserUniqueId` | `UNIQUEIDENTIFIER` | No | Entra `oid`; `AK_User_UserUniqueId` |
| `RoleId` | `INT` | No | `FK_User_Role` → `sec.Role(RoleId)`; indexed by `IX_User_RoleId`; default `1` (`DF_User_RoleId`) |
| `Name` | `NVARCHAR(300)` | No | Entire name, returned in API as `user_name` |
| `Email` | `NVARCHAR(300)` | No | Informational only; **never** use to identify/authorize users |
| `IsInternal` | `BIT` | No | Internal-company flag; default `1` (`DF_User_IsInternal`) |
| `IsActive` | `BIT` | No | Required for access; default `1` (`DF_User_IsActive`) |

> **Defaults note (DEC-03).** The three defaults above come from the table definitions already present in the repository and are kept on purpose. They are harmless in Sprint 1 because the API never inserts users and every seeded row states its values explicitly. They are **not** fail-closed (`IsActive` defaults to `1`, `RoleId` defaults to the first role): re-examine them before any user-provisioning feature exists.

The `UserUniqueId` uniqueness is based on the **ASSUMPTION** of a single configured Entra tenant. If multiple tenants are authorized later, the identity model must include a tenant identifier and use a tenant/oid composite authorization key (requires a reviewed schema change). Do not allow arbitrary tenants while keeping an `oid`-only lookup.

`Email` is **not** unique by default: email can change and is not the authoritative identity key.

#### `ref.Status`

| Column | SQL type | Null | Rules |
|---|---|---|---|
| `StatusId` | `INT IDENTITY(1,1)` | No | `PK_Status` |
| `Name` | `NVARCHAR(100)` | No | Internal name |
| `DisplayName` | `NVARCHAR(100)` | No | Human-readable label |
| `Code` | `NVARCHAR(20)` | No | Unique: `AK_Status_Code`; allowed `pending`, `ok`, `fail` (`CK_Status_Code`) |
| `Description` | `NVARCHAR(500)` | Yes | Explanatory text, optional |

Status `Code` is the source of the API `status` property; do not return the `Name` or `DisplayName` instead. Adding more status values is an explicit API/schema evolution.

#### `dbo.Demand`

| Column | SQL type | Null | Rules |
|---|---|---|---|
| `DemandId` | `INT IDENTITY(1,1)` | No | `PK_Demand` |
| `UserId` | `INT` | No | `FK_Demand_User` → `sec.User(UserId)`; indexed by `IX_Demand_UserId` |
| `SirenNumber` | `CHAR(9)` | No | Exactly nine ASCII digits, no punctuation (`CK_Demand_SirenNumber`) |
| `BaeLink` | `NVARCHAR(2048)` | No | Absolute HTTPS URL of the PDF in SharePoint; checked at least for the HTTPS prefix (`CK_Demand_BaeLink`) |
| `StatusId` | `INT` | No | `FK_Demand_Status` → `ref.Status(StatusId)`; indexed by `IX_Demand_StatusId` |

**SIREN is a string.** It is stored as text (`CHAR(9)`), carried as a string in the domain (`SirenNumber`), in the DTOs and in JSON — never as a number — so that leading zeros are preserved and display formatting stays separate from identity. The database check enforces **exactly nine digits** with a literal digit pattern (a `NOT LIKE '%[^0-9]%'` check alone would accept a shorter value padded with spaces). A SIREN checksum rule is **not** required in this sprint.

The SIREN identifies a *company*; `Demand.SirenNumber` MUST **NOT** be unique: several demands about the same company must remain possible.

`BaeLink` is an application-owned reference to a SharePoint document (OPEN-05). It is returned to the client unchanged once validated as an absolute HTTPS URL. The frontend must not construct document URLs itself.

### 3.4 Normative SQL DDL baseline

The SQL project MUST keep schemas and tables in separate files, as listed in §3.5. These definitions are the baseline that the files in `database/Klesia.Database/` implement; formatting may be adjusted without changing semantics.

**`Schemas/sec.sql`** and **`Schemas/ref.sql`**

```sql
CREATE SCHEMA [sec] AUTHORIZATION [dbo]
GO
```

```sql
CREATE SCHEMA [ref] AUTHORIZATION [dbo]
```

**`Tables/sec/Role.sql`**

```sql
CREATE TABLE [sec].[Role]
(
    [RoleId] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(300) NOT NULL,
    [Code] NVARCHAR(50) NOT NULL,
    CONSTRAINT [PK_Role] PRIMARY KEY CLUSTERED ([RoleId]),
    CONSTRAINT [AK_Role_Code] UNIQUE ([Code])
);
```

**`Tables/sec/User.sql`**

```sql
CREATE TABLE [sec].[User]
(
    [UserId] INT IDENTITY(1,1) NOT NULL,
    [UserUniqueId] UNIQUEIDENTIFIER NOT NULL,
    [RoleId] INT NOT NULL CONSTRAINT [DF_User_RoleId] DEFAULT 1,
    [Name] NVARCHAR(300) NOT NULL,
    [Email] NVARCHAR(300) NOT NULL,
    [IsInternal] BIT NOT NULL CONSTRAINT [DF_User_IsInternal] DEFAULT 1,
    [IsActive] BIT NOT NULL CONSTRAINT [DF_User_IsActive] DEFAULT 1,
    CONSTRAINT [PK_User] PRIMARY KEY CLUSTERED ([UserId]),
    CONSTRAINT [AK_User_UserUniqueId] UNIQUE ([UserUniqueId]),
    CONSTRAINT [FK_User_Role] FOREIGN KEY ([RoleId]) REFERENCES [sec].[Role] ([RoleId])
);
GO

CREATE NONCLUSTERED INDEX [IX_User_RoleId]
    ON [sec].[User] ([RoleId]);
GO
```

The index `IX_User` that existed on `UserId` (the primary key) was removed as redundant and replaced by `IX_User_RoleId`, which supports the `FK_User_Role` foreign key (DB-05).

**`Tables/ref/Status.sql`**

```sql
CREATE TABLE [ref].[Status]
(
    [StatusId] INT IDENTITY(1,1) NOT NULL,
    [Name] NVARCHAR(100) NOT NULL,
    [DisplayName] NVARCHAR(100) NOT NULL,
    [Code] NVARCHAR(20) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    CONSTRAINT [PK_Status] PRIMARY KEY CLUSTERED ([StatusId]),
    CONSTRAINT [AK_Status_Code] UNIQUE ([Code]),
    CONSTRAINT [CK_Status_Code] CHECK
        ([Code] COLLATE Latin1_General_100_BIN2 IN (N'pending', N'ok', N'fail'))
);
GO
```

**`Tables/dbo/Demand.sql`**

```sql
CREATE TABLE [dbo].[Demand]
(
    [DemandId] INT IDENTITY(1,1) NOT NULL,
    [UserId] INT NOT NULL,
    [SirenNumber] CHAR(9) NOT NULL,
    [BaeLink] NVARCHAR(2048) NOT NULL,
    [StatusId] INT NOT NULL,
    CONSTRAINT [PK_Demand] PRIMARY KEY CLUSTERED ([DemandId]),
    CONSTRAINT [FK_Demand_User] FOREIGN KEY ([UserId]) REFERENCES [sec].[User] ([UserId]),
    CONSTRAINT [FK_Demand_Status] FOREIGN KEY ([StatusId]) REFERENCES [ref].[Status] ([StatusId]),
    CONSTRAINT [CK_Demand_SirenNumber] CHECK
        ([SirenNumber] COLLATE Latin1_General_100_BIN2 LIKE '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'),
    CONSTRAINT [CK_Demand_BaeLink] CHECK ([BaeLink] LIKE N'https://%')
);
GO

CREATE NONCLUSTERED INDEX [IX_Demand_UserId]
    ON [dbo].[Demand] ([UserId], [DemandId] DESC);
GO

CREATE NONCLUSTERED INDEX [IX_Demand_StatusId]
    ON [dbo].[Demand] ([StatusId]);
GO
```

**Implementation notes:** `IX_Demand_UserId` leads with the foreign-key column (DB-05) and its descending `DemandId` serves the API ordering `DemandId DESC` without a sort. No `ON DELETE CASCADE` clause is permitted. The HTTPS `CHECK` is a simple guard only; code must separately parse and validate URLs. Future `Year`, `Month`, percentages and monetary amounts are not present in this sprint, so no speculative fields/checks are to be added.

### 3.5 Project layout, build and deployment

The existing `database/` folder and its conventions are preserved. Layout:

```text
database/
├── Directory.Build.props          # intentionally empty: isolates the SQL project from the .NET build settings
└── Klesia.Database/
    ├── Klesia.Database.sqlproj
    ├── PostDeployment.sql         # single aggregate post-deployment entry (project root)
    ├── Schemas/
    │   ├── sec.sql
    │   └── ref.sql
    ├── Tables/
    │   ├── sec/
    │   │   ├── Role.sql
    │   │   └── User.sql
    │   ├── ref/
    │   │   └── Status.sql
    │   └── dbo/
    │       └── Demand.sql
    ├── Scripts/
    │   ├── 01_RefData.sql
    │   └── 02_User.sql
    └── Properties/
        └── Azure.publish.xml
```

**Required `.sqlproj` capabilities:**

- Use an SDK-style project with a **pinned** `Microsoft.Build.Sql` version (currently `2.3.0`).
- Target Azure SQL Database explicitly through `Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider`.
- Build all table/schema definitions into the DACPAC as schema-model files.
- Include only one aggregate post-deployment entry, `PostDeployment.sql`, which references `01_RefData.sql` before `02_User.sql`.
- Exclude data scripts included by `:r` from the schema model (`Build Remove`) so they do not cause build errors or run twice.
- Do not put deployment credentials in `.sqlproj`, `.publish.xml`, source control or shell history.

```xml
<Project DefaultTargets="Build">
  <Sdk Name="Microsoft.Build.Sql" Version="2.3.0" />
  <PropertyGroup>
    <Name>Klesia.Database</Name>
    <DSP>Microsoft.Data.Tools.Schema.Sql.SqlAzureV12DatabaseSchemaProvider</DSP>
    <ModelCollation>1033, CI</ModelCollation>
  </PropertyGroup>
  <ItemGroup>
    <Build Remove="Scripts/**/*.sql" />
    <Build Remove="PostDeployment.sql" />
    <PostDeploy Include="PostDeployment.sql" />
  </ItemGroup>
</Project>
```

**`PostDeployment.sql`**

```sql
:r .\Scripts\01_RefData.sql
:r .\Scripts\02_User.sql
```

**Seed scripts (DEC-05).** Both scripts run after **every** successful publish, so they are idempotent:

- `01_RefData.sql` merges on the stable `Code`, never on an identity value. It guarantees the three status codes (`pending`, `ok`, `fail`) and the three confirmed roles (`admin`, `operational`, `guest`). Descriptive columns of reference rows are re-aligned by the deployment; nothing is deleted.
- `02_User.sql` is **insert-only**, matched on the Entra `oid` (`UserUniqueId`), with the role resolved by its code. An existing row is never updated, so a user deactivated by an administrator is not reactivated and administrator-edited fields are not overwritten; no user is deleted. It seeds the project owner's account as an active `admin` user. Further authorized identities are added by appending rows to that script, always with a valid, explicitly supplied Entra `oid`.

**Build:**

```powershell
dotnet build database/Klesia.Database/Klesia.Database.sqlproj -c Debug
```

**Publish (illustrative PowerShell; substitute a securely supplied connection string):**

```powershell
SqlPackage `
  /Action:Publish `
  /SourceFile:"database/Klesia.Database/bin/Debug/Klesia.Database.dacpac" `
  /Profile:"database/Klesia.Database/Properties/Azure.publish.xml" `
  /TargetConnectionString:$env:KLESIA_SQL_DEPLOY_CONNECTION
```

The target is Azure SQL Database, **not** SQL Server LocalDB. Real secrets remain outside tracked files. The publish profile keeps data-loss blocking enabled and never drops objects that are not in the source; review the generated script before applying it.

### 3.6 Database verification

The following checks are mandatory (the automated ones are in `tests/IntegrationTests/Database` and run when `KLESIA_TEST_SQL_CONNECTION` points to a disposable published database):

1. `dotnet build` produces the `.dacpac` at the documented path without unresolved references.
2. Publish to an **empty** Azure SQL dev/test database creates the `sec` and `ref` schemas and the four expected tables.
3. All `PK_*`, `FK_*`, `AK_*`, `CK_*`, `DF_*` and `IX_*` objects exist with the names defined by the DB-06 scheme.
4. Foreign keys reject orphan user-role, demand-user and demand-status references.
5. Duplicate `Role.Code`, `Status.Code` and `User.UserUniqueId` values fail; repeated `Demand.SirenNumber` values **succeed**.
6. SIREN values that are not exactly nine digits (letters, short, embedded space), non-HTTPS `BaeLink` and unsupported status codes fail.
7. Re-running the publish and reference scripts does not create duplicate seed data, wipe business records or reactivate inactive users.
8. An inactive user and a user without any demands can exist without breaking referential integrity.

## 4. Authentication and authorization — Sprint 1

Evaluation order for every protected request: **authentication** (token validity, `401`) → **delegated scope** (`403`) → **database allowlist** (`403`) → business query. The two `403` outcomes use the same status and an identical body.

### 4.1 Authentication contract (AUTH-01)

All Sprint 1 API routes MUST be protected by Microsoft Entra ID **access-token** authentication; the only anonymous routes are the health probe and, in Development, the OpenAPI document.

1. The frontend authenticates with Microsoft Entra ID and requests an **access token for the backend API** (not an ID token and not a Microsoft Graph access token).
2. The frontend sends `Authorization: Bearer <token>`.
3. The backend validates **signature**, **issuer**, **tenant**, **audience** and **lifetime** (`exp`, `nbf`). Any failure is `401`.
4. The backend then requires the configured **delegated scope** in the `scp` claim. A valid token that lacks it — including an application-only token, which has no `scp` — is `403`.
5. The backend reads the `oid` claim from the validated token and interprets it as the stable Entra user identifier. A missing or non-GUID `oid` is `401`.
6. The backend rejects tokens for other APIs or tenants, expired or unsigned tokens and ID tokens; it never trusts user IDs from query, body or arbitrary headers.
7. Use the established ASP.NET Core JWT bearer middleware; do not implement custom JWT decoding as authorization.

**Implementation decisions (AUTH-IMPL):**

- Package: `Microsoft.AspNetCore.Authentication.JwtBearer` with explicit `TokenValidationParameters`; claim-name mapping is disabled so `oid`, `scp` and `tid` keep their raw names.
- Authority and issuer: `{AzureAd:Instance}/{AzureAd:TenantId}/v2.0` (v2.0 tokens only). The API app registration MUST issue v2.0 access tokens (`requestedAccessTokenVersion = 2` in its manifest).
- Audience: `AzureAd:ClientId` and `api://{ClientId}` (overridable with `AzureAd:Audiences`).
- Tenant: besides the issuer, the `tid` claim MUST equal `AzureAd:TenantId`.
- ID tokens: a token carrying a `nonce` claim is rejected (`401`) even if its audience happened to match.
- Scope: an authorization policy (`DelegatedUser`) requires an authenticated user whose space-separated `scp` contains `AzureAd:RequiredScope`. The same policy is the **fallback policy**, so a new endpoint is protected unless it is explicitly marked anonymous.
- The allowlist check lives in the application layer (`DemandService`), not in the middleware, so token validation stays separate from business queries.

**ASSUMPTION:** single-tenant Entra application. The allowed tenant, authority, API audience and required delegated scope MUST come from configuration (§6.1). `oid` is unique **within** the validated tenant.

### 4.2 Database-based authorization (AUTH-02)

Only **after** successful token authentication and scope validation:

```text
validated access token (signature, iss, tid, aud, exp) ........ else 401
         |
required scope present in scp ................................. else 403
         |
         v
   extract token.oid ..... missing / not a GUID ............... 401
         |
         v
SELECT sec.User WHERE UserUniqueId = oid   (parameterized)
         |
         +-- not found ----------> 403 Forbidden
         +-- IsActive = 0 -------> 403 Forbidden   (identical response)
         +-- found + active -----> authorized user context
                                    (internal SQL User.UserId)
```

- A row in `sec.User` is an **allowlist entry**, not a provisioning request.
- `IsActive = 0` means denied, even with a cryptographically valid Entra token.
- **The only authorization rule is *own data only*** (OPEN-04): the authorized user receives exclusively the demands whose `Demand.UserId` equals their own `sec.User.UserId`.
- `IsInternal` is **not** an access gate in Sprint 1; it is informational until business rules are defined.
- `RoleId` must reference an existing `sec.Role` but grants **no elevated access and removes none** in Sprint 1.
- Do not create or update `sec.User` automatically during a request.
- Do not authorize using name, email, Entra group membership, frontend claims about roles or client-supplied user IDs.
- 403 responses MUST NOT disclose whether the account is unknown or disabled: same status, same title, same body shape.

### 4.3 HTTP status rules

This table is the single authority for authentication/authorization outcomes; §4.1, §4.2 and §9 follow it.

| Case | HTTP status | Reason |
|---|---:|---|
| Missing or malformed Authorization header | `401` | Not authenticated |
| Invalid signature, wrong issuer, wrong tenant (`iss` or `tid`), wrong audience, expired token, ID token | `401` | Authentication invalid |
| Valid token, `oid` claim missing or not a GUID | `401` | Identity cannot be established; fail closed |
| Valid token without the required delegated scope (including application-only tokens without `scp`) | `403` | Authentication valid, permission insufficient |
| Valid token and scope, user not present in `sec.User` | `403` | Not authorized |
| Valid token and scope, user present with `IsActive = 0` | `403` | Not authorized |
| Valid token and scope, active user present | Continue | Allowed to see **only own demands** |
| SQL dependency failure or other server fault | `5xx` | Service fault; never mislabel as `403` |

## 5. HTTP API — Sprint 1

### 5.1 `GET /api/v1/demands` (API-01)

**Purpose:** populate the initial dashboard screen with the authenticated user's display name/identifier and their demand list.

```http
GET /api/v1/demands HTTP/1.1
Authorization: Bearer <Entra access token>
Accept: application/json
```

**Parameters:** none. No user ID in path, query or request body. No role-specific or admin filters.

**Successful response:** `200 OK`, `Content-Type: application/json`

```json
{
  "user_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "user_name": "John DOE",
  "demands": [
    {
      "demand_id": 1,
      "siren_number": "732-829-320",
      "bae_link": "https://files.example.com/bae-1.pdf",
      "status": "pending"
    }
  ]
}
```

**Response schema:**

| JSON property | Type | Source / rule |
|---|---|---|
| `user_id` | string | Normalized GUID from validated Entra `oid` / matched `sec.User.UserUniqueId`. **Not** `sec.User.UserId`. |
| `user_name` | string | `sec.User.Name` from matched active DB row. |
| `demands` | array | All demand rows whose `Demand.UserId` is the matched internal `sec.User.UserId` (zero or more). |
| `demands[].demand_id` | integer | `dbo.Demand.DemandId`. |
| `demands[].siren_number` | string | `dbo.Demand.SirenNumber`, presented as `XXX-XXX-XXX`. The backend performs formatting. |
| `demands[].bae_link` | string | `dbo.Demand.BaeLink`: the **HTTPS URL** of the corresponding PDF in SharePoint, validated by the backend. It opens in the browser with the user's own SharePoint/Entra session, without a frontend-provided bearer Authorization header. |
| `demands[].status` | string enum | `ref.Status.Code`, exclusively `pending`, `ok`, `fail`. |

**Empty list is valid** and still returns `200`:

```json
{
  "user_id": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "user_name": "John DOE",
  "demands": []
}
```

**Backend processing rules:**

1. Authenticate bearer token and require the configured delegated scope.
2. Resolve an active `sec.User` using validated `oid`; otherwise return `403`.
3. Read demands only where `dbo.Demand.UserId = sec.User.UserId`; join `ref.Status` by `StatusId`.
4. Map the response to exactly the specified snake_case field names and types; no database rows or domain objects serialized directly.
5. Convert stored 9-digit SIREN to three groups separated by hyphens.
6. Return the stored status code verbatim (from the three allowed lowercase codes).
7. Resolve `BaeLink` through the link-resolver abstraction. In Sprint 1 the stored SharePoint URL is returned unchanged after verifying that it is an absolute HTTPS URL; a stored value that is not (data-integrity fault) produces a sanitized `5xx` and is never logged or returned. Never create anonymous links, never expose credentials, and never assume the frontend can attach the API bearer token when opening the PDF.
8. **ASSUMPTION:** order demands by `Demand.DemandId DESC` for deterministic display until a product ordering requirement is defined. The frontend MUST NOT treat this ordering as a permanent business contract.
9. Do not cap or paginate results in Sprint 1 without explicitly changing the API response contract. Flag performance concerns if volume makes this unsuitable.
10. Prevent cross-user data leakage, including in errors and logs.

**PDF access security note (OPEN-05, resolved):** the documents can contain sensitive personal data and live in SharePoint. The API is the gate to the *list* of links; SharePoint permissions are the gate to each *document*. Therefore the SharePoint library MUST NOT grant anonymous access, and users MUST hold SharePoint permission on the documents they are expected to open. Links are not bearer capabilities in this design, but they still identify documents and SHOULD be kept out of logs.

### 5.2 API errors and CORS (API-02)

- `400` / `422`: malformed input or applicable request validation, with only safe optional text. (The current GET has no input body and normally requires neither.)
- `401`: missing/invalid bearer authentication.
- `403`: valid but unauthorized caller, including unknown/inactive DB user or insufficient scope.
- `413`: reserved for a **future** PDF-upload endpoint; not applicable to the Sprint 1 read-only route.
- `5xx`: service and dependency faults; no exception stack traces, SQL statements, token contents or connection strings in HTTP bodies.
- **INT-08:** no fixed frontend-consumed error JSON contract yet. `ProblemDetails` MAY be used as a backend convention, but frontend correctness must depend on HTTP status rather than a mandatory `message` field.
- Configure CORS for **exact approved origins**, including deployed Azure Static Web App origin(s) and `http://localhost:5173`. Permit the needed `GET`, `POST`, `OPTIONS` methods and `Authorization`/`Content-Type` headers. Do not use an unrestricted `*` origin policy. The origins come from configuration (`Cors:AllowedOrigins`, §6.1) and are validated at startup; the API refuses to start if an origin is malformed or is `*`.
- Enforce CORS **before** authentication failures prevent browser preflight processing. A successful OPTIONS preflight is not an authorization grant.
- HTTPS is mandatory outside localhost. Validate proxy/forwarded-header settings only for trusted infrastructure.

## 6. Security, configuration, observability and operations

### 6.1 Environment configuration

Backend settings are bound as strongly typed options and validated at startup: the API refuses to start with missing or placeholder values (DEC-04).

| Area | Configuration key(s) and rules |
|---|---|
| Entra ID | `AzureAd:Instance` (default `https://login.microsoftonline.com/`), `AzureAd:TenantId`, `AzureAd:ClientId`, `AzureAd:RequiredScope` (scope name only, e.g. `access_as_user`), optional `AzureAd:Audiences`. All mandatory; placeholders are rejected at startup. |
| SQL | `ConnectionStrings:KlesiaDb`. Use `Authentication=Active Directory Default;Encrypt=True` so no password is stored: on a developer PC it signs in with the Azure CLI (`az login`) or Visual Studio account, in Azure with the managed identity. Prerequisites: the client IP is allowed in the Azure SQL firewall, and the Entra identity is a database user with read access (`CREATE USER [name] FROM EXTERNAL PROVIDER; ALTER ROLE db_datareader ADD MEMBER [name];`). |
| Frontend | `Cors:AllowedOrigins` — array of exact origins (`http://localhost:5173`, the deployed Static Web App URL). Validated at startup: no path, HTTPS required except loopback, `*` forbidden. Empty means no cross-origin access. |
| Configuration sources | Precedence, lowest to highest: `appsettings.json` (empty values, tracked) → `appsettings.{Environment}.json` (placeholders, tracked) → `appsettings.Local.json` (personal values, **git-ignored**) → environment variables (`AzureAd__TenantId`, `Cors__AllowedOrigins__0`, `ConnectionStrings__KlesiaDb`). |
| Logging | Log level, correlation id (`X-Correlation-Id`, generated when absent or unsafe), telemetry backend if already used. |
| Future SignalR | SignalR endpoint/resource name, managed identity configuration and hub identifier `demands` |
| Future PDF | `maxPdfSizeMb` pending confirmation; optional Microsoft Graph sharing-link settings if OPEN-05 evolves. Nothing is needed in Sprint 1: SharePoint links are returned as stored. |

Store credentials and connection secrets in environment-specific secret stores or managed identity configuration. Avoid checking credentials into `appsettings.json`, `.env` tracked files, Bicep parameter defaults, `Azure.publish.xml`, examples, log files or simulator artifacts.

### 6.2 Logs and operational behavior

- Structured request logs SHOULD include route, outcome/status, duration and correlation ID; do not log raw JWTs, authorization headers, document URLs, email addresses, document contents or complete connection strings.
- Log exceptions on the server with appropriate access controls; provide generic client errors.
- Fail closed on authentication/authorization failures; fail with `5xx` on actual database outages.
- Parameterize every SQL query through Dapper parameters. Never interpolate or concatenate `oid` or any user input into SQL text.
- Backend SQL credentials/managed identity SHOULD have minimum runtime permissions: read access to the four necessary tables; database-schema deployment uses a distinct, more privileged principal.
- Test CORS behavior from the frontend origin, including `OPTIONS` preflight and rejected authentication.

## 7. Preserved future API contracts — NOT to be implemented in Sprint 1

These requirements are frozen as intended integration interfaces for future sprints. They are not additional Sprint 1 deliverables, and their inclusion does not authorize inventing new routes.

### 7.1 `POST /api/v1/notifications/negotiate` (DEFERRED)

```http
POST /api/v1/notifications/negotiate HTTP/1.1
Authorization: Bearer <Entra access token>
```

Success: `200 OK`:

```json
{
  "url": "<Azure SignalR client endpoint>",
  "accessToken": "<short-lived connection token>"
}
```

- Apply **the same Entra token validation and database allowlist checks** as `GET /api/v1/demands`. A recognized but inactive user cannot negotiate a connection.
- Set the resulting SignalR `userId` to the validated Entra **`oid`**, not the SQL integer primary key, an email address or a frontend-supplied value.
- Use the Azure SignalR Service's `demands` hub.
- Base URL expected by the frontend: `VITE_SIGNALR_HUB_URL=<backend-base>/api/v1/notifications`. The standard SignalR JavaScript client appends `/negotiate` when connecting; **do not** put `/negotiate` inside that variable.
- The returned response is the **SignalR redirect negotiation** shape, not an ordinary arbitrary application JWT. Clients must not skip the negotiation step.
- Use backend managed identity to access Azure SignalR. The supplied infrastructure contract assigns **SignalR Service Owner** in Bicep, which matches a serverless negotiate + REST-publish use case; verify that resource mode and role assignments are consistent at implementation time. Never return the service access key to the browser.
- Explicitly configure API and Azure SignalR allowed frontend origins (Azure resource/Bicep where relevant).
- Do not add a separately exposed, unprotected `/negotiate` endpoint.

### 7.2 `demandUpdated` server-to-client event (DEFERRED)

| Property | Contract |
|---|---|
| SignalR hub | `demands` |
| Client method name | `demandUpdated` (case-sensitive) |
| Payload | **No arguments**; any unexpected payload is ignored by the frontend |
| Recipient | Owning user only, selected by stable Entra `oid` |
| Trigger | Demand status change; optionally a newly visible demand |
| Delivery | At-most-once, best effort; no replay or durable event history |
| Client recovery | Reload `GET /api/v1/demands` after (re)connection or receiving the event |

Do **not** broadcast demand activity to all connections. Future status-update integrations must establish the correct owner from the database or trusted business event, never from an untrusted client-supplied owner claim. No producer or status mutation endpoint has been specified yet.

### 7.3 Future upload and local simulator (DEFERRED)

- Upload request validation and gateway/backend payload size limits MUST eventually use the same confirmed `maxPdfSizeMb` value. The illustrative 10 MB is **not** a definitive acceptance criterion.
- No PDF-upload endpoint, multipart contract, request parameters or response shape have been provided; do not invent any in Sprint 1.
- `tools/dev-simulator/`, when implemented, should emulate the **specified** real routes and event behavior with the same response types, error codes and identity protections. At present the simulator is not implemented; do not count it as an existing dependency or Sprint 1 deliverable.

## 8. Implementation structure

The SQL database project location is required. The backend follows the Clean Architecture solution already in the repository (`Klesia.sln`); dependencies point inward only (`WebApi → Infrastructure → Application → Domain`).

```text
src/
  Domain/            Users/ApplicationUser, Demands/Demand, Demands/SirenNumber
  Application/       Abstractions/ (IUserRepository, IDemandRepository, IBaeLinkResolver)
                     Demands/ (DemandService, DemandDto, UserDemandsDto)
                     Common/Exceptions/ (ForbiddenException, NotFoundException)
  Infrastructure/    Persistence/ (ISqlConnectionFactory, repositories written with Dapper)
                     Documents/ (HttpsBaeLinkResolver)   Options/   DependencyInjection.cs
  WebApi/            Authentication/ (Entra options, validation, scope policy)
                     Cors/   Logging/   Contracts/ (snake_case response records)
                     Controllers/DemandsController   Infrastructure/GlobalExceptionHandler   Program.cs
tests/
  UnitTests/         domain and application behavior (NSubstitute)
  IntegrationTests/  real HTTP pipeline with signed test tokens and in-memory repositories;
                     opt-in SQL constraint tests (Database/)

database/
  Klesia.Database/   authoritative SQL project
```

Keep token validation separate from business query logic. Avoid introducing unrequested frameworks, microservices, event buses or CQRS infrastructure solely for Sprint 1. Keep DTO naming/JSON serialization independent from SQL table/column casing: JSON names are declared explicitly on the `WebApi` contract records.

## 9. Sprint 1 acceptance scenarios / test matrix

| ID | Given | When | Expected |
|---|---|---|---|
| AT-01 | No bearer token | GET `/api/v1/demands` | `401` |
| AT-02 | Expired, forged or wrong-audience token | GET | `401` |
| AT-03 | Token for Microsoft Graph or ID token instead of backend API access token | GET | `401` |
| AT-04 | Valid user access token missing required API scope | GET | `403` |
| AT-05 | Valid scoped token from unsupported Entra tenant | GET | Rejected (`401`) |
| AT-06 | Valid scoped token, `oid` not in `sec.User` | GET | `403` |
| AT-07 | Valid scoped token, matching `sec.User.IsActive = 0` | GET | `403` |
| AT-08 | Valid scoped token, active row, no demands | GET | `200`, correct `user_id` / `user_name`, `demands: []` |
| AT-09 | Valid scoped token, active row, multiple demands | GET | `200`; correct snake_case JSON, integer `demand_id`, formatted SIREN, HTTPS PDF link, allowed status code |
| AT-10 | Two authorized users with separate demands | Each GET | Each user receives only their own records; no cross-user leakage |
| AT-11 | Attacker sends `user_id` query/header matching another user | GET | Ignored for filtering; identity always from validated token |
| AT-12 | SQL database is unavailable | GET | `5xx`, sanitized response, logged server fault |
| AT-13 | Browser preflight from approved Vite/SWA origin | OPTIONS | Allowed CORS origin, methods and requested headers |
| AT-14 | Browser preflight from unapproved origin | OPTIONS | No permissive CORS authorization |
| AT-15 | Invalid SQL foreign key or invalid status/SIREN/link value | INSERT in test DB | Constraint rejection |
| AT-16 | Same company SIREN on distinct demands | INSERT | Allowed; no uniqueness on `Demand.SirenNumber` |
| AT-17 | SQL project republished with existing records | `SqlPackage /Action:Publish` | No unintended records deleted; idempotent seeds |
| AT-18 | Unauthorized user requests any future negotiation route, if introduced later | POST | Same `401`/`403` policy; **not an S1 test until route implemented** |
| AT-19 | Valid scoped token, active user, a stored `BaeLink` that is not an absolute HTTPS URL | GET | `5xx`, sanitized body; the stored value is not disclosed |
| AT-20 | Valid access token without an `oid` claim | GET | `401` |

**Definition of Done — Sprint 1**

- [ ] Four-table SQL project compiles to the specified `.dacpac`.
- [ ] Azure SQL publish procedure and repeat publish are verified in a safe dev/test environment.
- [ ] All declared relationships, checks, named constraints and FK indexes are in place.
- [ ] Existing reference-data scripts are reused rather than deleted/replaced blindly; they are idempotent and respect inactive accounts.
- [ ] Entra authentication rejects missing, malformed, expired, wrong-tenant, wrong-audience, and wrong-type tokens.
- [ ] The required delegated API scope is validated and the DB allowlist/active gate is enforced.
- [ ] `GET /api/v1/demands` matches the exact response JSON and does not leak other users' records.
- [ ] Local Vite and deployed SWA CORS are configured with explicit allowed origins.
- [ ] Safe errors, structured logs and no-credential-in-source checks are verified.
- [ ] Automated coverage exists for the Sprint 1 acceptance matrix; instructions to run/build/test/deploy are committed to the repository.
- [ ] Data access uses Dapper only: no Entity Framework Core package, `DbContext` or migration exists in the solution.
- [ ] The solution targets `net10.0`; AT-15, AT-16 and AT-17 are runnable against a disposable Azure SQL database (`KLESIA_TEST_SQL_CONNECTION`).
- [ ] Deferred SignalR/upload routes and the third endpoint are **not presented as delivered**; unresolved decisions remain recorded.

## 10. Implementation instructions for coding agents

1. **Inspect before changing:** inspect the existing repository tree, the SQL project, `01_RefData.sql`, `02_User.sql`, current frontend integration contracts, `.publish.xml` and infrastructure conventions. Do not overwrite initialized files unnecessarily.
2. **Implement only Sprint 1:** deliver the database plus `GET /api/v1/demands` with Entra access-token authentication and SQL allowlist authorization. Preserve, but do not implement, §7 future contracts.
3. **Avoid speculative features:** do not add a second or third HTTP route (the third endpoint is planned for a later sprint, OPEN-01), a status mutation API, an upload API, user-admin CRUD, role hierarchies, additional tables or SignalR stubs that return fake production data.
4. **Follow contracts verbatim:** preserve table names, schema names, the constraint-naming scheme of DB-06, JSON field names, HTTP statuses and identity mapping.
5. **Dapper only (DEC-02):** do not add Entity Framework Core packages, a `DbContext`, entity configurations or migrations. Write parameterized SQL in `Infrastructure` repositories; schema changes go through the SQL project.
6. **Keep credentials external:** do not use illustrative server names/passwords as executable defaults, and do not include real Entra access tokens or real document URLs in tests/logs. Personal configuration goes into the git-ignored `appsettings.Local.json` or environment variables.
7. **Validate correctness:** run builds/tests, inspect the generated DACPAC, and verify a repeat publish on a disposable/dev SQL database when access is available. If Azure access is unavailable, report the unverified deployment step explicitly rather than claiming completion.
8. **Report changes:** at the end, provide changed-file list, build/test results, configuration values required from the client, unresolved decisions and any deviations from this specification.

## 11. External technical references

These are implementation references, not new business requirements:

- SQL database projects and `Microsoft.Build.Sql`: https://learn.microsoft.com/en-us/sql/tools/sql-database-projects/sql-database-projects
- SQL-project Azure SQL target platform (`DSP`): https://learn.microsoft.com/en-us/sql/tools/sql-database-projects/concepts/target-platform
- SQL pre/post-deployment script handling: https://learn.microsoft.com/en-us/sql/tools/sql-database-projects/concepts/pre-post-deployment-scripts
- Entra access token validation: https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens
- Entra claim validation / tenant-scoped identity: https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation
- API scope authorization (`Microsoft.Identity.Web`): https://learn.microsoft.com/en-us/entra/msidweb/authentication/authorization
- Azure SignalR negotiate: https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-concept-client-negotiation
- Azure SignalR managed identity and RBAC: https://learn.microsoft.com/en-us/azure/azure-signalr/signalr-howto-authorize-managed-identity

---

**Change policy for Sprint 2+:** Add a sprint section with new requirements and tests. Modify prior contracts only through an explicit compatibility/versioning decision. Do not retroactively recast deferred endpoints as Sprint 1 completed work.
