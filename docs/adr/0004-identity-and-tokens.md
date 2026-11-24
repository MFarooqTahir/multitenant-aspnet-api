# 0004 — ASP.NET Core Identity with custom JWTs and rotating refresh tokens

- **Status:** Accepted
- **Date:** 2026-10-24

## Context

The API needs password hashing, lockout, roles, email confirmation and TOTP-based two-factor
authentication. It also needs stateless access tokens for horizontal scaling and a way to
revoke sessions, which pure JWTs cannot do.

## Decision

Use the full ASP.NET Core Identity stack (`UserManager`, `SignInManager`, stores, token
providers) but **not** `MapIdentityApi`. Authentication is a hand-written JWT flow:

- **Access token** — HMAC-SHA256 JWT, 15 minutes, containing `sub`, `tenant_id`,
  `tenant_slug`, `role`, `email`, `name`, `sstamp` and `jti`. Issued only after Identity has
  verified the password (and the second factor when enabled).
- **Refresh token** — 256 bits of CSPRNG output, URL-safe, stored only as a SHA-256 hash. It
  expires after 30 days and belongs to a *family*. Logout only revokes a token owned by the
  caller, so an authenticated user cannot end someone else's session.
- **Lockout covers the second factor** — the same Identity counters protect the password and
  the TOTP/recovery-code check, and a locked account cannot complete a 2FA challenge even
  with the correct code.
- **Rotation** — refreshing marks the old token revoked and issues a replacement in the same
  family. Presenting an already-revoked token is treated as theft: the whole family is
  revoked and the request fails.
- **Live invalidation** — every authenticated request compares the `sstamp` claim with the
  user's current security stamp and the `role` claim with the current role. Role changes,
  logout-everywhere, password resets and single-session logins therefore invalidate existing
  access tokens immediately instead of waiting for expiry.
- **Atomic session transitions** — refresh rotation, logout-all and new-session issuance wrap
  the security-stamp change, refresh-token revocation and token insert in one database
  transaction. A failed revocation rolls the stamp change back; a failure-injection test uses
  a PostgreSQL trigger to prove it.
- **Enumeration resistance** — unknown tenant/user login paths verify a fixed dummy password
  hash, and the confirmed-email check runs only after a successful password check.
- **Single session (default)** — `Auth:SingleSessionPerUser=true`. A successful login or
  refresh rotates the security stamp and revokes the user's other refresh tokens, so only the
  most recent session works. Setting it to `false` allows concurrent sessions.
- **Same email in different tenants** — `RequireUniqueEmail=false`; `UserName` is namespaced
  as `{tenant-slug}:{email}` and uniqueness is enforced by a composite unique index on
  `(TenantId, NormalizedEmail)`. Login resolves `TenantId + NormalizedEmail`.

## Consequences

- Access token validation costs one indexed database read per request. That is the price of
  immediate revocation, and it is easy to cache later if needed.
- Refresh tokens are opaque values: a database leak does not leak usable tokens.
- Two-factor setup calls Identity's `ResetAuthenticatorKeyAsync`, which rotates the security
  stamp. Clients must re-authenticate after setup and before enabling the first code. This is
  intentional, and the integration test documents the flow.
- `MapIdentityApi` was rejected because its cookie + bearer model, routes and errors cannot be
  shaped to the tenant-aware contract without fighting the framework.

## Alternatives considered

- **Pure stateless JWT** — cannot revoke tokens, rejected.
- **Opaque session tokens for everything** — one extra lookup per request and no standard
  bearer semantics; rejected as the primary mechanism.
- **Identity's built-in API** — no tenant slug on login, no refresh rotation, rejected.
