# 0016 — Optimistic concurrency for projects and tasks

- **Status:** Accepted
- **Date:** 2026-11-25

## Context

Project and task updates were last-write-wins. Two callers could read the same row, make
different edits, and both commit successfully; the later save silently discarded the earlier
one. Deletes could similarly race with updates or child inserts.

The last-Owner guard has a different requirement: concurrent role changes must serialize before
counting Owners. That uses the tenant row lock. Ordinary project/task edits do not need that
contention; they need detection of a stale read.

## Decision

Use Npgsql's mapping of a `uint` row-version property to PostgreSQL's existing `xmin` system
column:

- `Project` and `WorkItem` map a shadow `xmin` property with `IsRowVersion()`.
- EF includes the original `xmin` in the `UPDATE`/`DELETE` predicate. A changed row raises
  `DbUpdateConcurrencyException`.
- The API maps that exception to `409 concurrency_conflict`; the client reloads and retries.
- The migration is intentionally empty: every PostgreSQL table already has `xmin`, so no DDL is
  emitted. It records the updated model snapshot and migration history entry.
- ETags and `If-Match` are not added yet. The database detects lost updates, but clients do not
  yet send an explicit expected version.

## Consequences

- Concurrent project/task writes can no longer silently overwrite each other.
- No schema column or index is added, and normal single-writer requests are unchanged.
- PostgreSQL serialization failures and deadlocks during `SaveChanges` are also reported by
  Npgsql 11 as `DbUpdateConcurrencyException` and receive the same 409 response.
- Pessimistic tenant locks remain necessary for aggregate invariants such as last-Owner
  protection; optimistic concurrency is not a replacement for those checks.
