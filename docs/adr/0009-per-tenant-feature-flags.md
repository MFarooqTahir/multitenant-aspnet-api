# 0009 — Per-tenant feature flags

- **Status:** Accepted
- **Date:** 2026-10-25

## Context

The project plan calls for per-tenant feature flags as a stretch goal. Before this change the
only feature switches (`Identity:RequireConfirmedEmail`, `Identity:EnableTwoFactor`) were
deployment-wide settings: turning one off for a single tenant meant a redeploy, and turning one
on for everybody.

A multi-tenant SaaS needs a narrower switch: a tenant decides whether a capability that is
compiled in and available to everyone else is active **for that tenant**. The mechanism must not
become a general experimentation platform, and it must not change behaviour for tenants that
never touch it.

## Decision

Store a small, whitelisted set of feature overrides per tenant.

1. **Whitelist, not free-form keys.** `TenantFeatureKeys` lists every switchable feature
   (`task_assignment` today). Unknown keys are rejected with `404 unknown_feature`, so a typo
   cannot create a flag that nothing reads.
2. **Absent means enabled.** A tenant with no `TenantFeatures` row for a key behaves exactly as
   before. Flags are per-tenant kill switches, not opt-ins, so deploying a new feature never
   silently disables it for existing tenants.
3. **Owner-only writes, member reads.** `PUT /tenants/current/features/{key}` is restricted to
   the `Owner` role at the controller and re-checked in `TenantFeatureService`; any member may
   read the effective values.
4. **Consumers behave as if the capability does not exist.** A disabled feature returns
   `404 feature_disabled` (with a specific code such as `task_assignment_disabled`), matching the
   existing rule that the API does not advertise capabilities a caller cannot use.
5. **One real consumer from day one.** `task_assignment` gates assigning a user to a task in
   `TaskService`; creating or updating an unassigned task is unaffected. This keeps the
   mechanism honest instead of adding a flag nothing checks.

## Consequences

- Reads cost one indexed lookup per request; the composite primary key `(TenantId, Key)` and the
  global query filter keep it tenant-safe.
- Adding a feature means adding a key to the whitelist and a consumer, not a schema change.
- The stored row is an override, so "enabled" is not necessarily materialised. Listing the flags
  reports the effective value for every known key.
- This is not a per-user entitlement system and not an A/B testing platform. Per-resource
  permissions remain a separate concern (see the roadmap in `README.md`).
