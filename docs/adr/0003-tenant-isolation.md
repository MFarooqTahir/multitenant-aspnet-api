# 0003 — Tenant isolation in one database

- **Status:** Accepted
- **Date:** 2026-10-24

## Context

A multi-tenant SaaS API must make cross-tenant data leaks practically impossible, not merely
unlikely. Common strategies are database-per-tenant (strong isolation, expensive operations),
schema-per-tenant (medium isolation, migration complexity) and a shared schema with a
`TenantId` column (cheap operations, relies on discipline).

## Decision

Use a shared schema with `TenantId` on every tenant-owned table, enforced at several layers:

1. **Tenant resolution** — the `tenant_id` claim in the access token is the only source of
   truth. Subdomains and client-supplied headers are not trusted. Services that run before a
   token exists (login, refresh, onboarding) resolve the tenant explicitly and call
   `ITenantContext.SetTenant` before touching tenant-owned data.
2. **Read isolation** — EF Core global query filters on `AppUser`, `Project` and `WorkItem`.
   The filter references a `DbContext` property (`CurrentTenantId`), not the injected
   `ITenantContext` closure, because EF caches the model per context type and a captured
   closure would freeze the first request's tenant.
3. **Write isolation** — a `SaveChanges` interceptor stamps `TenantId` on inserts and rejects
   inserts/updates/deletes whose `TenantId` differs from the resolved tenant.
4. **Referential isolation** — `WorkItem → Project` uses a composite foreign key on
   `(TenantId, ProjectId)`, and `WorkItem → AppUser` uses `(AssigneeTenantId, AssigneeUserId)`
   with a check constraint requiring `AssigneeTenantId = TenantId`. A task cannot reference
   another tenant's project or user even if the application guard were bypassed.
5. **API semantics** — cross-tenant reads and writes return **404**, not 403, so the API
   does not confirm that a resource exists in another tenant.

Live authorization additionally re-checks the user's security stamp and current role on
every authenticated request (see ADR 0004), so changes take effect immediately.

## Consequences

- One database keeps onboarding, migrations, backup and local development simple.
- Every new tenant-owned entity must implement `ITenantOwned`; integration tests verify the
  boundaries.
- Queries that intentionally cross tenants (login, refresh, security-stamp validation) use
  `IgnoreQueryFilters()` **plus** an explicit `TenantId` predicate. This is a deliberate,
  greppable exception.
- A future move to database-per-tenant would keep the Application contracts unchanged.
