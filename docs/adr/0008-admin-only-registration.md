# 0008 — Registration is a tenant-admin action, not anonymous self-service

- **Status:** Accepted
- **Date:** 2026-10-25

## Context

The original `POST /auth/register` was anonymous: anyone who knew a tenant's slug could create
a `Member` account and then read every project and task in that tenant. A slug is a public,
guessable identifier (it appears in URLs and documentation), so this was equivalent to treating
the slug as a shared password. Neither an invite, an email proof nor the tenant's consent was
required.

The tenant already had an admin path (`POST /users`, Owner/Admin only) that creates users with
an explicit role and sends the confirmation email.

## Decision

Require the `manage_tenant` policy (Owner or Admin) on `POST /auth/register`:

- The caller must be authenticated and hold the Owner or Admin role **in the target tenant**.
- The body still carries `tenantSlug`, but it is validated against the tenant resolved from the
  access token. A mismatch returns `403 tenant_mismatch`, so an admin of one tenant cannot
  create users inside another.
- The created account is always `Member`; promotion happens through `PATCH /users/{id}/role`.
- The endpoint now sends the confirmation email, matching `/users` and onboarding.
- Anonymous joining is removed rather than made opt-in. Public sign-up, when a product needs
  it, should be a signed invite or an email-domain allow-list — something that proves the
  tenant consented — not a flag that turns the old hole back on.

## Consequences

- A tenant slug is not a credential: knowing it grants no access to data. The only ways into a
  tenant are onboarding (which creates the tenant) and an existing Owner/Admin.
- `POST /auth/register` and `POST /users` now overlap: the former is the fixed-role shortcut for
  adding a Member, the latter is the full admin surface. They share `IUserProvisioningService`,
  so creation rules stay in one place.
- The endpoint keeps the IP rate limiter as defence in depth, even though it is authenticated.
- Tests assert the new behaviour: anonymous `401`, Member `403`, cross-tenant slug `403`.
