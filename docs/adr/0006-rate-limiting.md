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
- **`auth` policy** — 10 requests/minute partitioned by client IP, applied to login,
  refresh, register, resend-confirmation, two-factor verification and tenant onboarding.
  It runs alongside the global limiter, giving auth endpoints the stricter of the two.

Options are resolved per request from `IOptions<RateLimitingOptions>`, so configuration
overrides (including test hosts) take effect without rebuilding the policy.

Rejections return `429` with `Retry-After` and an RFC 7807 body (`code: rate_limited`).

## Consequences

- No extra infrastructure for the portfolio deployment.
- State is per process: with multiple replicas the effective limit is per replica. ADR notes
  this as a known limitation; production would move to a distributed limiter.
- The global limiter runs after authentication so it can see the tenant claim.
