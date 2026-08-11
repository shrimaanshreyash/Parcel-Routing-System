# Domain project responsibility

This project contains the pure, deterministic parcel-routing policy. It has no
package dependency and does not reference ASP.NET Core, Entity Framework Core,
PostgreSQL, React, XML or file parsers, logging providers, clocks, or network
clients.

## Implemented model

- `Weight`, `DeclaredValue`, and `CountryCode` protect parcel-input invariants.
- `Parcel` holds only routing facts and deliberately excludes recipient personal
  data.
- `WeightBandRule` assigns Mail, Regular, or Heavy from constrained intervals.
- `InsuranceApprovalRule` adds a workflow hold above an exclusive EUR threshold.
- `RoutingRuleSet` validates gaps, overlaps, fallbacks, identifiers, priorities,
  and versioning before it can evaluate a parcel.
- `RoutingDecision` records department, approval state, matched rule identifiers,
  rule-set version, correlation metadata, and ordered explanations.

The caller supplies decision time and correlation ID through
`RoutingDecisionContext`; the domain does not generate nondeterministic values.
