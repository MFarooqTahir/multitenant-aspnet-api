# 0012 — Stay on .NET 10 LTS until the providers ship .NET 11 support

- **Status:** Superseded by [0013](0013-dotnet-11-with-ef-core-10.md)
- **Date:** 2026-11-18

> **Superseded:** the application now targets `net11.0`. The part of this decision that still
> holds is the stable-only rule for the EF Core data stack; ADR 0013 records the current target.

## Context

The portfolio plan asks for the current **LTS** .NET release. .NET 10 is that release; .NET 11
has shipped but is a short-term-support release. A .NET 11 migration was attempted and stops at
restore time for two dependencies:

| Dependency | State on 2026-11-18 | Effect |
| --- | --- | --- |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `10.0.3` stable; `11.0.0-rc.3` only | The RC pins `Microsoft.EntityFrameworkCore` to `11.0.0-rc.3`; ASP.NET Core Identity 11.0.0 requires the stable `11.0.0`, so restore fails with **NU1107**. |
| `Asp.Versioning.OpenApi` | `10.2.3` (latest) | Requires `Microsoft.OpenApi >= 2.7.5, < 3.0.0`; `Microsoft.AspNetCore.OpenApi` 11.0.0 requires `Microsoft.OpenApi >= 3.11.0` (**NU1608**), so per-version OpenAPI documents break. |

The alternatives were:

1. Pin the whole data stack to `11.0.0-rc.*` packages.
2. Replace the Asp.Versioning OpenAPI integration with hand-written per-version documents.
3. Stay on .NET 10 LTS until the ecosystem catches up.

## Decision

Stay on `net10.0` and record the upgrade gate. .NET 10 is supported into November 2028, and the
plan explicitly prefers LTS. Application code targets the framework, not the TFM, so the
upgrade is mechanical once the gate clears:

1. Bump the TFM in `Directory.Build.props` and the central versions in
   `Directory.Packages.props`.
2. Move the Docker base images and the CI SDK to 11.x.
3. Re-run the test suite; the .NET 11 breaking-change list does not touch this codebase (no
   Cosmos, no span collection expressions, no `NamedPipeClientStream`, no `BackgroundService`).

## Consequences

- No release-candidate dependency in a portfolio project, and no bespoke OpenAPI plumbing.
- The project is one PR away from .NET 11 when `Npgsql.EntityFrameworkCore.PostgreSQL` 11.0.0
  and an Asp.Versioning release that supports `Microsoft.OpenApi` 3.x are published.
- Reviewers see the decision and its evidence in the README and here, rather than a stale badge.

## Alternatives considered

- **Release-candidate data stack** — rejected: a DB provider RC and EF Core RC together is a
  poor signal, and the upstream package graph does not even resolve with stable Identity.
- **Mixed package majors on `net11.0`** — rejected: ASP.NET Core 10 packages on the 11 shared
  framework is unsupported and hides the real upgrade work.
- **Drop API versioning to avoid Asp.Versioning** — rejected: URL-segment versioning is a plan
  requirement, not a workaround target.
