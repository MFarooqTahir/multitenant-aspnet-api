# 0013 — Target .NET 11 with the stable EF Core 10 data stack

- **Status:** Superseded by [0014](0014-dotnet-11-data-stack.md) for the data stack; the OpenAPI pin remains in force
- **Date:** 2026-11-18

> **Update (2026-11-25):** Npgsql.EntityFrameworkCore.PostgreSQL 11.0.0 is now stable. ADR 0014
> moves EF Core, Identity, health checks and Npgsql to 11.0.0. The Microsoft.OpenApi 2 /
> Asp.Versioning.OpenApi constraint described here still applies. The current SDK policy is
> `11.0.100` with `latestPatch`; Docker runtime/build images are digest-pinned to `11.0.0`.

## Context

.NET 11 reached general availability on 2026-11-10 (SDK `11.0.100`, ASP.NET Core `11.0.0`,
EF Core `11.0.0`). The project targets it. The hard constraint is the portfolio rule that every
dependency must be a **stable** release: no previews, RCs or nightly builds.

Two package families cannot yet satisfy both rules:

| Dependency | Stable state | Blocker |
| --- | --- | --- |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `10.0.3` (latest stable); `11.0.0-rc.3` exists | The 10.0.3 package declares `Microsoft.EntityFrameworkCore >= 10.0.4, < 11.0.0`. The 11.0.0 RC pins EF Core to `11.0.0-rc.3`, which cannot coexist with stable ASP.NET Core Identity 11 (NU1107). |
| `Asp.Versioning.OpenApi` | `10.2.3` | Requires `Microsoft.OpenApi < 3.0.0`; `Microsoft.AspNetCore.OpenApi` 11.0.0 requires `Microsoft.OpenApi >= 3.11.0` (NU1608). |

The rest of the stack (ASP.NET Core, logging, test host, Serilog) has stable 11.0.0 GA
releases.

## Decision

Target `net11.0` and the .NET 11 GA toolchain while keeping only the EF Core-bound packages on
their stable 10.0.x line:

- SDK `11.0.100` (the original decision used `rollForward: latestFeature`; the current policy is
  `latestPatch`), Docker images `11.0.0` runtime / `11.0.100` SDK, CI SDK `11.0.100`.
- `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Mvc.Testing` and
  `Serilog.AspNetCore` move to `11.0.0`.
- EF Core `10.0.13`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` `10.0.13`,
  `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` `10.0.13`,
  `Microsoft.Extensions.Identity.Stores` `10.0.13` and Npgsql `10.0.3` stay on the stable 10
  line.
- `Microsoft.AspNetCore.OpenApi` stays at `10.0.13` so it remains compatible with
  `Asp.Versioning.OpenApi` 10.2.3 and `Microsoft.OpenApi` 2.x.
- The nine `Microsoft.Extensions.*` assemblies that are now part of the `net11.0` shared
  framework are no longer referenced as packages (NU1510), and the per-project
  `TargetFramework` duplicates are removed in favour of `Directory.Build.props`.

No package with a prerelease suffix is used.

## Consequences

- The application runs on .NET 11 (GA, supported through November 2028) and uses .NET 11
  ASP.NET Core packages for authentication, the test host and logging.
- EF Core 10 runs on the .NET 11 runtime. EF Core 11-only features are unavailable until the
  provider ships; no EF Core 11 API is used.
- The upgrade is one Central Package Management bump: when Npgsql `11.0.0` is stable, move EF
  Core, Identity.EFCore, HealthChecks.EFCore, Identity.Stores and Npgsql to 11.0.x, then decide
  whether to move the OpenAPI stack once an Asp.Versioning release supports Microsoft.OpenApi 3.
- The .NET 11 breaking-change list was reviewed: no Cosmos, Blazor, MAUI, NativeAOT, named
  pipe, span collection-expression, `BackgroundService` or TAR/ZIP usage applies. Kestrel's
  stricter protocol compliance and the OpenTelemetry server-span defaults do not affect this
  API's tested surface.

## Alternatives considered

- **All-11 stack with Npgsql `11.0.0-rc.3`** — rejected: violates the stable-only rule and the
  RC pins EF Core to the RC.
- **Stay on `net10.0`** — superseded: the user/plan target is .NET 11 now that it is GA.
- **Drop API versioning to take ASP.NET Core OpenAPI 11** — rejected: versioned routes and
  per-version documents are a plan requirement; the 10.0 OpenAPI package is stable and works on
  `net11.0`.
