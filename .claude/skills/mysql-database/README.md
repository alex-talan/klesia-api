# MySQL Database

Human-facing guide for the `mysql-database` skill.

## Purpose

The KLESIA database is **Azure Database for MySQL (Flexible Server, MySQL 8.4)**, not SQL Server (specification DEC-14). This skill keeps agents and engineers on the MySQL dialect, the table-prefix naming, the DbUp migration workflow and the passwordless Microsoft Entra authentication.

## When To Use It

- Adding or changing a table, constraint or index (new migration script)
- Changing reference data or the authorized users (seed scripts)
- Writing SQL in an Infrastructure repository
- Changing the connection string, Entra authentication or `AZURE_CLIENT_ID`
- Writing database constraint tests

## Quick Reference

| Topic | Rule |
|-------|------|
| Tables | `sec_Role`, `sec_User`, `ref_Status`, `Demand` in one database (no schemas in MySQL) |
| Schema owner | `database/Klesia.Database` (DbUp): `Migrations/` run once, `Seeds/` run every time |
| Changing the schema | New numbered script, one DDL object per script, never edit an applied one |
| Dialect | Backticks, `LIMIT`, `LAST_INSERT_ID()`. Never T-SQL |
| Auth | Entra access token is the password. No `Password` in connection strings |
| Tests | `KLESIA_TEST_DB_CONNECTION` on a disposable MySQL 8.4 database |

## Typical Prompt

```text
Use the mysql-database skill to add a CreatedAtUtc column to Demand and update the spec and tests.
```
