# 0014 — Complete the .NET 11 data-stack migration

- **Status:** Accepted
- **Date:** 2026-11-25
- **Supersedes:** the EF Core 10 portion of [0013](0013-dotnet-11-with-ef-core-10.md)

> **Update (2026-11-25):** [ADR 0017](0017-openapi-3-and-postgresql-18.md) supersedes the
> OpenAPI portion below: `Microsoft.AspNetCore.OpenApi` is now 11.0.0 and the incompatible
> `Asp.Versioning.OpenApi` bridge has been removed.

## Context

ADR 0013 kept EF Core on 10.0.x because Npgsql had not shipped a stable EF Core 11 provider.
That changed on 2026-11-25: `Npgsql.EntityFrameworkCore.PostgreSQL` 11.0.0 is stable and declares
`Microsoft.EntityFrameworkCore >= 11.0.0, < 12.0.0`. The stable-only upgrade gate is clear.

## Decision

Move the whole data stack to 11.0.0 through the existing CPM properties:

- `DataStackVersion=11.0.0`: EF Core, EF Core Design, Identity.EntityFrameworkCore,
  Identity.Stores and the EF Core health-check integration.
- `NpgsqlVersion=11.0.0`: Npgsql and its EF Core provider.
- Replace EF Core 11's obsolete `ExecuteSqlInterpolatedAsync` with `ExecuteSqlAsync` for the
  parameterized tenant row lock.
- Keep `Microsoft.AspNetCore.OpenApi` 10.0.13 and `Asp.Versioning.OpenApi` 10.2.3: the latter
  still requires `Microsoft.OpenApi < 3.0.0`, while ASP.NET Core OpenAPI 11 requires 3.11+.

The data-stack version bump itself required no schema change; the existing migrations are applied
by EF Core 11 in the Testcontainers suite. The later `RefreshTokenIntegrityAndRetention` migration
is an independent integrity improvement, not a requirement of the provider upgrade.

## Consequences

- The application, ASP.NET Core packages and the relational data stack are all on .NET 11 GA.
- No preview/RC dependency remains.
- EF Core 11 query/translation improvements apply automatically.
- The OpenAPI stack was the only remaining 10.x package family at the time, isolated behind
  `OpenApiVersion`; ADR 0017 subsequently moved it to the built-in .NET 11 generator.

## Alternatives considered

- **Stay on EF Core 10** — rejected: the stable provider is available and the mixed transitive
  Microsoft.* graph is no longer necessary.
- **Use Npgsql 11.0.0-rc.3** — rejected: stable 11.0.0 now exists and the repo is stable-only.
- **Move OpenAPI to 11 at the same time** — was blocked by Asp.Versioning.OpenApi's
  `Microsoft.OpenApi < 3.0.0` constraint; ADR 0017 later removed that bridge and adopted the
  built-in generator.
