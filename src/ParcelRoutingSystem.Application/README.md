# Application project responsibility

This project orchestrates routing, approval, batch, and rule-lifecycle use cases.
It owns persistence, clock, and identifier ports while remaining independent of
ASP.NET Core, Entity Framework Core, PostgreSQL, XML, and React.

## Implemented use cases

- Route one parcel through the active immutable rule set.
- Persist or replay decisions using normalized request fingerprints.
- Approve an insurance hold through an append-only idempotent action.
- Accept parsed batches, isolate invalid rows, and process valid rows under
  restart-recoverable leases.
- Defer retryable rows when no active policy is available.
- Draft, simulate, activate, and roll back constrained rule-set versions.
- Produce privacy-safe audit records that persistence writes atomically with
  each state change.
