# Infrastructure project responsibility

This project implements inward-owned application ports without defining routing
rules.

## Implemented responsibilities

- EF Core 10 and Npgsql PostgreSQL mappings.
- Reviewed migrations for the default active rule set, country provenance, and
  operational-history indexes.
- Transactional immutable decisions and append-only approvals.
- Transactional privacy-safe audit events.
- Immutable rule drafts and atomic activation or rollback.
- Durable batches with `FOR UPDATE SKIP LOCKED`, claim tokens, expiry recovery,
  row isolation, and aggregate progress.
- Unique idempotency constraints plus normalized request fingerprints.
- Secure streaming XML parsing with document and row safety boundaries.
- Bounded operational queries for overview, history, approvals, import
  attention, and activity.

API composition, authentication, authorization, and hosted worker execution
remain in the API project. Production telemetry exporters remain a client
deployment boundary.
