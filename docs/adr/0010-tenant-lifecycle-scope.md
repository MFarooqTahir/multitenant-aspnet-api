# 0010 — Tenant lifecycle stays tenant-scoped

- **Status:** Accepted
- **Date:** 2026-10-25

## Context

The project plan lists tenants alongside users, projects and tasks. Onboarding, the current-tenant
read and a get-by-id endpoint already exist, but there is no way to rename a tenant, list tenants,
or deactivate one.

Two of those are tempting to add and wrong for this design:

- **A cross-tenant list** (`GET /tenants`) has no caller for whom it is safe. The API has no
  platform-administrator role, so the only candidate is a tenant Owner — and showing them every
  other tenant's name and slug would break the rule that the API never confirms that another
  tenant exists.
- **A self-service deactivation endpoint** would be irreversible. Authentication already rejects
  inactive tenants (`AuthService`, `JwtBearerConfiguration`), and there is no platform admin to
  reactivate one, so an Owner who clicked the wrong button would lock out their whole
  organisation with no supported recovery path.

## Decision

1. Add `PUT /tenants/current` so an Owner can rename the current tenant.
2. Keep the slug immutable: it is the login key, and changing it would silently break every
   user's sign-in and every stored confirmation link.
3. Do **not** add a cross-tenant tenant list.
4. Do **not** add a tenant deactivation endpoint until there is an operator model (platform
   admin, support tooling or a signed recovery flow). Deactivation remains an operations action
   against the database, documented rather than exposed.

## Consequences

- Tenant administration stays inside the isolation model: a caller can only act on the tenant
  the token resolves to.
- `GET /tenants/{id}` keeps returning the caller's tenant only; other ids remain `404`.
- If a platform-administrator role is introduced later, a tenant list and a reversible
  deactivate/reactivate pair become reasonable additions; the current contracts do not block it.
- The README's limitations section states this scope explicitly so reviewers do not read the
  absence as an oversight.
