# 0017 — OpenAPI 3.2 and PostgreSQL 18 migration

- **Status:** Accepted
- **Date:** 2026-11-25

## Context

`Microsoft.AspNetCore.OpenApi` 11 generates OpenAPI 3.2 and depends on `Microsoft.OpenApi` 3.x.
The latest stable `Asp.Versioning.OpenApi` package, 10.2.3, still requires `Microsoft.OpenApi`
2.x, so the previous integration could not be upgraded without a package conflict.

PostgreSQL 18 is stable and supported by Npgsql 11. The application has no production data, and
the local Compose database is disposable, so the database major-version upgrade does not need a
production data conversion.

## Decision

### OpenAPI

- Remove `Asp.Versioning.OpenApi` and upgrade `Microsoft.AspNetCore.OpenApi` to 11.0.0.
- Keep `Asp.Versioning.Mvc` and `Asp.Versioning.Mvc.ApiExplorer` 10.2.1 for URL-segment routing,
  API Explorer grouping and `{version}` substitution.
- Register one built-in document named `v1` with `AddOpenApi("v1", ...)` and serve it at
  `/openapi/{documentName}.json`.
- Suppress AV0029/AV0030 for the API project with an explicit justification: those diagnostics
  require the incompatible bridge, while this application currently publishes one version.
- Keep the bearer security-scheme transformer; it compiles unchanged against the OpenAPI 3.x
  object model.
- Use Scalar 3.2 for the API reference UI.

### PostgreSQL

- Use the digest-pinned `postgres:18-alpine` image in Compose and mount the named volume at
  `/var/lib/postgresql`, matching the PostgreSQL 18 image's major-version subdirectory layout.
- Run Testcontainers against `postgres:18-alpine` and assert the server major version in tests.
- Allow Dependabot to propose future PostgreSQL updates again.
- Require `docker compose down -v` for disposable local data, or `pg_dump`/`pg_restore` or
  `pg_upgrade` when an existing PostgreSQL 16 volume must be retained.

## Consequences

- `/openapi/v1.json` is OpenAPI 3.2 and Scalar renders it successfully.
- No EF Core migration is required for the database server upgrade; the schema is unchanged.
- Adding another API version requires registering another built-in OpenAPI document or adopting
  a future compatible Asp.Versioning OpenAPI bridge.
- Existing PostgreSQL 16 named volumes cannot be reused directly by PostgreSQL 18.
