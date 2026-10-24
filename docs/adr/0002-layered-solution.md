# 0002 — Layered solution with a thin API

- **Status:** Accepted
- **Date:** 2026-10-24

## Context

The API needs to demonstrate separation of concerns, testability and a dependency direction
that keeps business rules independent of frameworks. A single project is simpler, but hides
the boundaries that make a codebase maintainable as it grows.

## Decision

Four projects with a one-way dependency graph:

```
Multitenant.Api            (HTTP, auth pipeline, OpenAPI, composition root)
  └── Multitenant.Infrastructure   (EF Core, Identity, JWT, email, tenancy plumbing)
        └── Multitenant.Application (contracts, abstractions, validation, options)
              └── Multitenant.Domain (entities, invariants, enums, domain errors)
```

- **Domain** has no framework dependencies except the Identity base classes for the user and
  role entities, because EF Identity is the chosen persistence model.
- **Application** defines what the system can do (`IAuthService`, `IProjectService`, …) and
  the shapes crossing the boundary. It does not know about EF Core or ASP.NET Core.
- **Infrastructure** implements those interfaces. It is the only project that knows about
  PostgreSQL, Npgsql, Identity stores, SMTP and JWT signing.
- **Api** is intentionally thin: controllers delegate to Application interfaces, and the
  pipeline handles cross-cutting concerns. There is no business logic in controllers.

## Consequences

- Unit tests can exercise validators, domain rules and token generation without a database.
- Integration tests exercise the real pipeline and a real PostgreSQL instance.
- The extra projects cost a little ceremony but make the dependency direction explicit.
- Mapping is kept simple (no AutoMapper); hand-written projections keep queries efficient.
