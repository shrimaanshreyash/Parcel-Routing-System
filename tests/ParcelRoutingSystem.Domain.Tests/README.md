# Domain test responsibility

This project verifies the pure routing policy without starting a web host,
database, XML parser, or infrastructure adapter.

## Test suites

- `DefaultRoutingBoundaryTests` protects exact and adjacent 1 kg and 10 kg cases.
- `InsuranceApprovalTests` protects the strict EUR 1,000 boundary and confirms
  insurance never replaces Mail, Regular, or Heavy.
- `ParcelValidationTests` rejects invalid weight, value, country, attributes, and
  default value-object bypasses.
- `RoutingDecisionTests` protects determinism, explanations, rule identifiers,
  versions, country independence, and attribute independence.
- `RoutingRuleSetSafetyTests` rejects gaps, overlaps, duplicate identifiers,
  duplicate priorities, missing catch-alls, invalid departments, and invalid
  versions while proving a safe versioned threshold change.
- `RoutingInvariantTests` evaluates dense deterministic weight and value samples.
- `ReferenceManifestDecisionCorpusTests` protects all 17 privacy-safe reference
  weight/value combinations through synthetic parcel facts.
