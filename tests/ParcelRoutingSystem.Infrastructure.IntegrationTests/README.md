# PostgreSQL integration test responsibility

This project applies the production migration to a disposable PostgreSQL 17
container and verifies:

- durable decision and approval replay after fresh EF contexts;
- concurrent idempotent routing convergence;
- batch lease recovery after a simulated worker restart;
- atomic rule activation and rollback;
- rollback of a decision when its audit insert fails;
- migration-model consistency against the real Npgsql provider.
- privacy-minimized legacy-XML parsing, row-country retention, malformed and
  DTD rejection, and configured row limits.

The container contains synthetic non-personal test data and is removed after the
test collection finishes.
