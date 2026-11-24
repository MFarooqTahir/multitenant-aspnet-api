# 0015 — Bounded refresh-token retention with a hosted cleanup job

- **Status:** Accepted
- **Date:** 2026-11-25

## Context

Expired and revoked refresh-token rows previously accumulated forever. The table is small per
user but grows without bound across tenants, and old token families have little operational
value after the retention window.

## Decision

Add an in-process retention job:

- `RefreshTokenCleanupOptions` under `Auth:RefreshTokenCleanup` controls `Enabled`,
  `IntervalHours` (default 24), `RetentionDays` (default 30), `BatchSize` (default 1,000) and
  `MaxBatchesPerPass` (default 100), all validated at startup.
- `RefreshTokenCleanupService` is a `BackgroundService`. It runs once at startup and then on a
  `PeriodicTimer`, creates a DI scope for each pass, and uses `RefreshTokenCleaner`.
- The cleaner deletes rows whose expiry is at least 30 days old, or whose revocation is at
  least 30 days old, in bounded ID batches using `ExecuteDeleteAsync`. Leading expiry/revocation
  indexes support the scan.
- Refresh tokens use a composite `(TenantId, UserId)` foreign key, so a token can never point at
  a user in another tenant even if application code is bypassed.
- A failed pass is logged and retried on the next tick. It never escapes `ExecuteAsync`, because
  .NET 11 stops the host when a `BackgroundService` throws.
- The job is disabled in the integration-test host; the cleaner itself is tested against real
  PostgreSQL.

## Consequences

- Refresh-token storage is bounded by active sessions plus a 30-day forensic window.
- The operation is idempotent and safe if several replicas run it at once; they may duplicate a
  pass, but cannot corrupt data.
- At much larger scale, move the schedule to an external job or add a PostgreSQL advisory lock;
  the cleaner's set-based delete remains the same.

## Alternatives considered

- **Keep every row forever** — rejected: unbounded growth and no operational benefit.
- **Delete immediately on expiry/revocation** — rejected: removes the audit window and makes
  failure investigation harder.
- **External scheduler/worker** — sound at scale, but unnecessary infrastructure for a
  single-service portfolio deployment.
