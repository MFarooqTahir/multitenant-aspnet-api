# 0001 — Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-10-24

## Context

This repository is a portfolio project meant to demonstrate production practices, not just
working code. The reasoning behind the important choices should be reviewable alongside the
implementation, so that a reader can tell *why* something is the way it is and what was
deliberately traded away.

## Decision

Every architecturally significant decision is captured as a short Architecture Decision
Record in `docs/adr/`. Records describe context, decision, consequences and alternatives.

Each record describes the decision the code makes **today**: when a decision changes, the
record is updated so the docs never describe superseded behaviour. This repository is a
portfolio piece, not an audit trail, so keeping the set small and accurate matters more than
preserving every intermediate state. New decisions get a new numbered record.

## Consequences

- Reviewers can evaluate trade-offs without reverse-engineering the code.
- The set of ADRs doubles as a checklist for future work.
- Small decisions stay in code comments instead of ADRs, to avoid noise.
