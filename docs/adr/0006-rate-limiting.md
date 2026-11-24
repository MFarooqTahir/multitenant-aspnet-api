# 0006 — Per-tenant rate limiting with the built-in limiter

- **Status:** Accepted
- **Date:** 2026-10-24

## Context

A noisy tenant must not be able to starve other tenants. Authentication endpoints are also a
natural brute-force target and need a stricter budget than normal traffic. A distributed
limiter (Redis) is the right answer at scale, but adds infrastructure the project does not
otherwise need.

## Decision

Use ASP.NET Core's built-in `RateLimiter` middleware with a global limiter and one
stricter named policy:

- **Global limiter** — 120 requests/minute partitioned by the `tenant_id` claim, falling
  back to the client IP for anonymous requests. Because the partition key is the tenant, one
  tenant exhausting its budget cannot affect another.
- **`auth` policy** — 10 requests/minute partitioned by client IP, applied to the endpoints
  that accept credentials or can trigger an email: login, two-factor verification,
  resend-confirmation and tenant onboarding. It runs alongside the global limiter, giving
  those endpoints the stricter of the two.
- **Routine authenticated or session-maintenance calls stay on the global budget.** Refresh
  and `POST /auth/register` are not credential guesses: refresh happens on a timer for every
  client, and a shared per-IP bucket would let one office NAT (or a login rush) lock out
  legitimate sessions. Register is already tenant-scoped by the global partition; refresh
  falls back to the IP partition until a tenant is known. Both remain bounded, just not at
  the login-sized limit.
- **Health probes get a separate per-IP global partition.** They do not consume a tenant's
  API budget, but readiness checks still cannot drive unlimited database queries.

Options are resolved per request from `IOptions<RateLimitingOptions>`, so configuration
overrides (including test hosts) take effect without rebuilding the policy.

Rejections return `429` with `Retry-After` and an RFC 7807 body (`code: rate_limited`).

## Consequences

- No extra infrastructure for the portfolio deployment.
- State is per process: with multiple replicas the effective limit is per replica. ADR notes
  this as a known limitation; production would move to a distributed limiter.
- The global limiter runs after authentication so it can see the tenant claim.
