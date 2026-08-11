# Parcel Routing System: 10-15 Minute Demo Walkthrough

## 1. Frame the problem - 1 minute

- The system routes parcels by weight: Mail through 1 kg, Regular above 1
  through 10 kg, Heavy above 10 kg.
- Declared value above EUR 1,000 adds an insurance hold; it never replaces the
  intended department.
- The privacy-safe fixtures cover country fallback, valid duplicate rows, and
  the legacy recipient element without exposing real recipient data.
- Design goal: deterministic, explainable, safe for a non-technical operator.

## 2. Explain the architecture - 2 minutes

- React presents facts and server results; it owns no routing thresholds.
- ASP.NET Core enforces authentication, authorization, rate limits, upload
  bounds, safe errors, and health.
- Application use cases own orchestration, idempotency, approval, batches, and
  rule lifecycle.
- The framework-free domain owns validation, typed rules, deterministic
  evaluation, and explanations.
- PostgreSQL stores immutable decisions, append-only evidence, rule versions,
  audit events, and lease-protected batch rows.
- Nginx serves one same-origin reviewer application.

## 3. Route two parcels - 2 minutes

Open `New parcel`.

1. Route 5 kg, EUR 520, Colombia.
   - Point out Regular, no approval, version 1, and plain-language reasons.
2. Route 0.9 kg, EUR 1,500, Netherlands.
   - Point out intended Mail plus Awaiting insurance.
   - Explain that physical release waits; the department decision is already
   deterministic.

On `Overview`, point out that the main KPI is the all-time number of persisted
decisions while the note reports today's UTC evaluations. Switch Decision
history from `Recent 10` to `All time`, open page 2, and explain that filtering
and 15-item paging happen in PostgreSQL rather than loading unlimited data.

## 4. Import a privacy-safe XML fixture - 3 minutes

Open `Import XML`.

1. Select `tests\fixtures\xml\02-valid-variations.xml`.
2. Choose Netherlands as the fallback for the row with no country.
3. Import.
4. Confirm that all three rows are evaluated and none fail.
5. Point out that Evaluated means validated, decided, and persisted - not
   dispatched, delivered, or approved.
6. Navigate away and back or refresh; reopen the durable recent batch.
7. Submit the same source again.
   - Show the prior batch warning.
   - Explain operation replay versus deliberate confirmed re-import.
   - Confirm only if another batch is useful; valid duplicate rows are never
     silently deleted.

## 5. Approve insurance - 2 minutes

Open `Insurance`.

- Open the oldest decision.
- Review EUR value, destination, intended department, version, rules, reasons,
  timestamp, and batch relation.
- Approve as the role-enabled Development reviewer.
- Show append-only actor/time evidence and the immediate queue decrement.
- Explain that the API independently forbids an ordinary Operator.

## 6. Demonstrate safe rule change - 2 minutes

Open `Routing rules`.

- Change only a typed boundary, for example Mail from 1 to 1.2 kg.
- Save the draft: domain validation protects gaps, overlaps, and coverage.
- Simulate: explain the representative before/after outcome.
- Activate version 2, then roll back to version 1.
- Point out that old decisions keep their original version and explanations.
- Emphasize that there is no scripting or runtime AI.

## 7. Close with reliability and limits - 1-2 minutes

- 139 automated tests; Release build with zero warnings/errors.
- DTD/external XML prohibited; raw XML and recipient data are not persisted.
- Idempotent operations, durable rows, expiring leases, transaction rollback,
  approval replay, atomic rule activation.
- Development uses `Local reviewer`; Production requires real OIDC values and
  cannot fall back.
- Compose restart preserved PostgreSQL state; live and ready health return 200.
- Honest limits: client OIDC deployment, production telemetry/alerts,
  environment load test, and full WCAG audit are not claimed.

## Likely interviewer questions

### Why not a general rules engine?

Four typed rules do not justify arbitrary expressions, a second programming
model, or a larger security surface. The implementation keeps the valuable
parts: stable IDs, immutable versions, validation, simulation, audit, and
rollback.

### Why is insurance not a department?

Weight determines the intended physical destination. Value creates an
independent workflow prerequisite. Combining them would lose information and
make approval behavior confusing.

### Why not silently deduplicate the XML?

The source has no trusted parcel ID and valid duplicate rows. Silent
deduplication would corrupt input. A manifest fingerprint warns at the operation
level while preserving all rows.

### What happens after a crash?

Accepted batches and rows are already in PostgreSQL. Workers claim rows with
tokens and expiring leases. A restarted worker can reclaim expired work without
duplicating completed decisions.

### How would you deploy authentication?

Register the API audience and browser client with the client's OIDC provider,
map the three allow-listed roles, supply authority/audience through the secret
and configuration platform, and keep Production startup migrations disabled.

### How would you add a country rule?

Add a new typed condition and semantic validation in the domain, extend the
draft contract deliberately, add representative simulation and authorization
tests, migrate stored rule definitions if needed, and never make existing
historical decisions re-evaluate silently.
