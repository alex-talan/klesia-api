---
name: mysql-database
description: Azure Database for MySQL 8.4 rules for Klesia - MySQL dialect (never T-SQL), table prefixes instead of schemas, DbUp migrations and idempotent seeds, utf8mb4 collation, CHECK constraints, passwordless Microsoft Entra token authentication, and constraint tests. Use when writing or reviewing SQL, migrations, seed scripts, connection settings, or database tests.
---

# MySQL Database (Klesia)

The Klesia database is **Azure Database for MySQL – Flexible Server, MySQL 8.4** (specification DEC-14). It replaced SQL Server / Azure SQL Database at the customer's request, so any habit or snippet from T-SQL is a bug here. `docs/specs/SPECIFICATIONS.md` section 3 and `CLAUDE.md` always win over this skill.

## When to Use This Skill

- Writing or reviewing a migration or a seed script in `database/Klesia.Database`
- Writing or reviewing SQL in an Infrastructure repository
- Touching the connection string, Microsoft Entra database authentication or `AZURE_CLIENT_ID`
- Writing database constraint tests
- Answering "how do I do X from SQL Server in MySQL"

Related skills: `dotnet-backend-patterns` (Dapper repositories, DI, tests), `azure-integration` (managed identity), `security-audit` (secrets, injection), `testing-patterns`.

## Fixed Decisions

| Topic | Rule |
|-------|------|
| Engine | Azure Database for MySQL Flexible Server, MySQL **8.4**. 8.0 is not targeted (standard support ends 2027-01-31). `CHECK` needs 8.0.16+ |
| Driver | `MySqlConnector` 2.4.0 with Dapper. No `Microsoft.Data.SqlClient`, no Oracle `MySql.Data`, no EF Core |
| Database layout | One database `klesia`. **No schemas**: tables are `sec_Role`, `sec_User`, `ref_Status`, `Demand` (`sec_`/`ref_` prefixes, business tables unprefixed) |
| Schema owner | The DbUp migrator `database/Klesia.Database`: `Migrations/` (run once, journaled in `SchemaVersions`) and `Seeds/` (run every time) |
| Engine/charset | InnoDB, `utf8mb4`, `utf8mb4_0900_ai_ci`, declared on every table |
| Types | `INT AUTO_INCREMENT` keys, `VARCHAR(n)` text, `CHAR(36)` GUID (lower-case canonical), `TINYINT(1)` flags, `CHAR(18)` GK number |
| Authentication | Passwordless: no `Password` in any connection string; the Microsoft Entra access token is the password |
| Transport | TLS always (`SslMode=Required`, `VerifyFull` once the CA chain is confirmed) |

## T-SQL to MySQL Cheat Sheet

| SQL Server (do not write) | MySQL (write this) |
|---------------------------|--------------------|
| `[sec].[User]`, `[Name]` | `` `sec_User` ``, `` `Name` `` (backticks; spell identifiers exactly as in the spec) |
| `SELECT TOP 10 ...` | `SELECT ... LIMIT 10` |
| `OFFSET @o ROWS FETCH NEXT @n ROWS ONLY` | `LIMIT @n OFFSET @o` |
| `INT IDENTITY(1,1)` | `INT NOT NULL AUTO_INCREMENT` |
| `SCOPE_IDENTITY()`, `OUTPUT INSERTED.Id` | `SELECT LAST_INSERT_ID()` on the **same** open connection (no `RETURNING`) |
| `NVARCHAR(n)` | `VARCHAR(n)` (the table charset is `utf8mb4`; no `N'..'` prefix) |
| `UNIQUEIDENTIFIER`, `CONVERT(UNIQUEIDENTIFIER, N'..')` | `CHAR(36)` and a plain lower-case string literal |
| `BIT` | `TINYINT(1)` plus a `CHECK (col IN (0, 1))` |
| `ISNULL(a, b)` | `COALESCE(a, b)` or `IFNULL(a, b)` |
| `GETUTCDATE()` | `UTC_TIMESTAMP(6)`; store UTC in `DATETIME(6)` |
| `a + b` for strings | `CONCAT(a, b)` |
| `LEN(x)` | `CHAR_LENGTH(x)` (`LENGTH` counts bytes) |
| `MERGE ... WHEN MATCHED / NOT MATCHED` | `UPDATE ... JOIN` plus `INSERT ... SELECT ... WHERE NOT EXISTS` |
| `UPDATE d SET ... FROM t d JOIN ...` | `UPDATE t AS d INNER JOIN ... SET d.col = ... WHERE ...` |
| `LIKE 'GK[0-9][0-9]...'` | `REGEXP_LIKE(col, '^GK[0-9]{6}-[0-9]{9}$', 'c')` (`c` = case-sensitive) |
| `COLLATE Latin1_General_100_BIN2` | `COLLATE utf8mb4_bin` |
| `GO` batch separator, `:r file.sql` | Not used: one script = statements separated by `;` |
| `CREATE SCHEMA`, `dbo.` | Not available: use the table-name prefix |
| `CREATE TYPE ... AS TABLE` (TVP) | Not available: Dapper expands `WHERE Id IN @Ids` |
| Named `PK_` / `DF_` constraints | Not nameable: the primary key is always `PRIMARY`, defaults are inline |
| `Authentication=Active Directory Default` | Entra token as the password (see below) |

## Migration Rules (DbUp)

```
database/Klesia.Database/
├── Klesia.Database.csproj      stand-alone console project, pinned package versions
├── Program.cs                  DbUp runner
├── Migrations/0001_Create_sec_Role.sql ...   run ONCE, ordered by file name
└── Seeds/01_RefData.sql, 02_User.sql         run on EVERY execution, after the migrations
```

1. **Add, never edit.** A change to the schema is a new script with the next number (`0005_Add_Demand_CreatedAt.sql`). A script that has been applied in any shared environment is immutable, because DbUp records it by name and will not run it again.
2. **One DDL object per script.** MySQL DDL commits implicitly and cannot be rolled back: a script with several DDL statements can fail half-way and leave the database in a state no script describes.
3. **Naming:** `{4-digit number}_{Verb}_{Object}.sql`. Scripts are embedded automatically (`Migrations\*.sql`), no project edit is needed.
4. **No semicolon inside a comment or a string literal**: the DbUp splitter cuts statements on it.
5. Keep ASCII in identifiers; non-ASCII text in data (`'Traitée'`) is fine because scripts are UTF-8 and the connection is `utf8mb4`.
6. **Destructive changes** (drop, narrowing a type, rename) need an explicit review. The migrator has no data-loss blocking and the deployment principal deliberately has no `DROP`/`DELETE` grant: a destructive migration also needs a deliberate grant.
7. Adding a `NOT NULL` column to a populated table: add it with a `DEFAULT` (or nullable), backfill in a later script, then tighten in a third script.
8. New constraints and indexes follow the DB-06 naming scheme: `FK_{Table}_{ReferencedTable}`, `AK_{Table}_{Column}`, `CK_{Table}_{Column}`, `IX_{Table}_{Column}`, with `{Table}` the unprefixed logical name. Names of foreign keys and checks are unique per database.
9. Declare the index that serves a foreign key in the same `CREATE TABLE`, with the foreign-key column leading, so MySQL reuses it and does not create a second, auto-named one.

```sql
-- Migrations/0005_Add_Demand_CreatedAt.sql  (example, not part of the baseline)
ALTER TABLE `Demand`
    ADD COLUMN `CreatedAtUtc` DATETIME(6) NOT NULL DEFAULT (UTC_TIMESTAMP(6));
```

Run: `dotnet build database/Klesia.Database/Klesia.Database.csproj`, then `dotnet run --project database/Klesia.Database` with `KLESIA_DB_DEPLOY_CONNECTION` set (see `docs/local-development.md`).

## Seed Rules

- Seeds run after **every** execution: they must be idempotent and must never delete rows.
- Match on the stable business key (`Code`, `UserUniqueId`), never on an `Id`.
- Pattern: `UPDATE ... INNER JOIN (SELECT ... UNION ALL SELECT ...) AS s ON s.Code = r.Code SET ... WHERE <something differs>` to re-align descriptive columns, then `INSERT ... SELECT ... FROM (...) AS s WHERE NOT EXISTS (...)`.
- **Do not use `INSERT IGNORE`** (it also swallows unrelated errors such as truncation) **or `ON DUPLICATE KEY UPDATE`** (it advances `AUTO_INCREMENT` for every existing row, on every run).
- `Seeds/02_User.sql` is **insert-only**: an existing user (even deactivated or edited by an administrator) is never touched. To authorize another identity append a `UNION ALL SELECT` line with a lower-case, canonical Entra `oid`.
- Literals rely on the connection collation being the utf8mb4 default (`utf8mb4_0900_ai_ci`), which is Azure's default; use `COLLATE utf8mb4_bin` when a comparison must be exact.

## Constraint Rules

- `CHECK` constraints are enforced from MySQL 8.0.16. On an older server they are parsed and **silently ignored**: always verify the server version in the test run.
- Collation is case- and accent-insensitive (`_ai_ci`). Where case matters, say so in the constraint: `` `Code` COLLATE utf8mb4_bin IN (...) `` or `REGEXP_LIKE(..., 'c')`.
- `CHAR(n)` values are space-padded and trailing spaces are stripped on read, so a short value can never satisfy an exact-length pattern; keep the explicit `CHAR_LENGTH` guard.
- `FOREIGN KEY` without `ON DELETE`/`ON UPDATE` is `RESTRICT`: no cascades (DB-09).
- Errors to translate in Infrastructure: 1062 duplicate key, 1452 child row has no parent, 1451 parent is referenced, 3819 check constraint violated, 1048 column cannot be null, 1406 data too long.

## Authentication to the Database (Microsoft Entra)

- Azure Database for MySQL accepts a Microsoft Entra **access token as the password**. The user name is the Entra database user (or alias). The token resource is `ossrdbms`: scope `https://ossrdbms-aad.database.windows.net/.default`. Tokens for other audiences stop working from January 2027.
- A token is valid 5 to 60 minutes: the API refreshes it through `MySqlDataSourceBuilder.UsePeriodicPasswordProvider` (every ~10 minutes, retry after ~5 seconds); the migrator fetches one at start-up.
- The client sends the token in clear text (the server needs it unhashed), therefore **TLS is mandatory**: `SslMode=Required` at least.
- Locally the credential is `DefaultAzureCredential` (your `az login` or Visual Studio account). In Azure it is the **user-assigned** managed identity (the only kind Flexible Server supports); set the environment variable `AZURE_CLIENT_ID` on the container app (Azure Container Apps, DEC-15) so the credential picks it.
- A password in a connection string is a defect: not in `appsettings*.json`, not in the migrator command line, not in logs. The presence of a `Password` switches off the token provider, so it must never be added "to make it work".
- Principals: developer (read), API managed identity (`SELECT`, plus `INSERT` on `Demand` once creation ships), deployer (`SELECT, INSERT, UPDATE, CREATE, ALTER, INDEX, REFERENCES`; no `DROP`/`DELETE`). `CREATE AADUSER` grants nothing by itself. Exact statements: `docs/local-development.md` (the managed-identity form is not yet verified on a real server, spec OPEN-07).

## Pitfalls

- **Table-name case.** The server may store table names in lower case (`lower_case_table_names`). Never create two tables that differ only by case and always spell a name the same way.
- **Implicit commit.** DDL (and some statements) end the current transaction. Never mix DDL into a DML transaction.
- **`LAST_INSERT_ID()` is per session.** Open the connection explicitly before the `INSERT`; Dapper closes a connection it opened itself after each call and the pool may hand a different session to the next one.
- **Affected rows.** With MySqlConnector's default `UseAffectedRows=false` an `UPDATE` reports matched rows, not changed rows.
- **Time.** `DATETIME` has no time zone: store UTC in `DATETIME(6)`. Avoid `TIMESTAMP` (2038 limit).
- **Strict mode.** Keep the server `sql_mode` strict so truncation and invalid values fail instead of warning. Do not relax it for a script.
- **Index key size.** InnoDB indexes are limited to 3072 bytes: with `utf8mb4` that is 768 characters, so a long `VARCHAR` cannot be indexed whole (`BaeLink` is deliberately not indexed).
- **`LIKE` and user text.** Escape `%` and `_` before using user input in a `LIKE` pattern.
- **GUIDs.** Store and compare the canonical lower-case 36-character form; the `CK_User_UserUniqueId` check rejects anything else.
- **Multi-statement commands.** MySqlConnector allows them, but only the first result set is read by `QuerySingle*`; keep one statement per command unless you use `QueryMultiple`.
- **Managed-service limits.** Do not rely on `SUPER` or `FILE` privileges: a managed Flexible Server does not hand them out.

## Testing

Database constraint tests need a real, disposable MySQL 8.4 database already migrated by the migrator. They are gated by `KLESIA_TEST_DB_CONNECTION` and every test runs in a transaction that is rolled back (DML only).

```csharp
// xUnit 2.x: skip, instead of fail, when no database is configured.
public sealed class RequiresDatabaseFactAttribute : FactAttribute
{
    public RequiresDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KLESIA_TEST_DB_CONNECTION")))
        {
            Skip = "Set KLESIA_TEST_DB_CONNECTION to run the database tests.";
        }
    }
}

public class DatabaseConstraintTests
{
    [RequiresDatabaseFact]
    public async Task Insert_ShouldFail_WhenGKNumberPrefixIsLowerCase()
    {
        await using MySqlConnection connection = new(Environment.GetEnvironmentVariable("KLESIA_TEST_DB_CONNECTION"));
        await connection.OpenAsync();
        await using MySqlTransaction transaction = await connection.BeginTransactionAsync();

        Func<Task> action = () => connection.ExecuteAsync(
            """
            INSERT INTO Demand (UserId, GKNumber, BaeLink, StatusId)
            SELECT u.Id, 'gk260902-144743112', 'https://example.com/bae.pdf', s.Id
            FROM sec_User AS u, ref_Status AS s
            WHERE s.Code = 'pending'
            LIMIT 1
            """,
            transaction: transaction);

        (await action.Should().ThrowAsync<MySqlException>()).Which.Number.Should().Be(3819);
        await transaction.RollbackAsync();
    }
}
```

Cases to cover (specification section 3.6): foreign keys reject orphans; duplicate `Role.Code`, `Status.Code`, `User.UserUniqueId` fail; repeated `Demand.GKNumber` succeeds; wrong GK format (lower-case prefix, letters, short, trailing space, missing hyphen), non-HTTPS `BaeLink`, unsupported or wrong-case status code, malformed GUID and flags other than 0/1 fail with 3819; seeds are idempotent and re-running the migrator changes nothing; the server version is at least 8.0.16.

## Review Checklist

- [ ] SQL is MySQL dialect: backticks, `LIMIT`, `LAST_INSERT_ID()`; no `TOP`, `[ ]`, `OUTPUT`, `MERGE`, `GO`
- [ ] Table and column names match the specification exactly (`sec_User`, `ref_Status`, `Demand`)
- [ ] Schema change = new numbered script, one DDL object, no applied script edited, no semicolon in comments
- [ ] Constraint names follow DB-06; FK-leading index present; no cascade
- [ ] Seeds idempotent: no `INSERT IGNORE`, no `ON DUPLICATE KEY UPDATE`, no deletes, users insert-only
- [ ] Every statement parameterized; no string-built SQL
- [ ] No password anywhere; TLS required; token scope is `ossrdbms`
- [ ] Constraint tests added for every new check or key, and they ran on MySQL 8.4
