# Routing Rule System

## Decision

Build a small constrained rule system owned by the application. Do not adopt Drools, Microsoft RulesEngine, Easy Rules, json-rules-engine, or NRules as a dependency for the initial scope.

## Why

- The initial policy has three weight bands and one approval condition.
- A general-purpose engine would add a second programming model and a larger security surface.
- Arbitrary expressions are difficult to validate, explain, and safely expose to non-technical administrators.
- The design should remain understandable enough for safe live modification.
- We can retain the valuable rule-engine concepts without importing unnecessary runtime complexity.

## Concepts retained from mature engines

- Stable rule identifiers.
- Explicit priority.
- Typed, allow-listed conditions.
- Separate routing and approval effects.
- Deterministic evaluation.
- Immutable versions.
- Validation before activation.
- Decision explanations.
- Simulation against a golden and recent parcel corpus.
- Audit and rollback.

## Implemented vocabulary

Conditions:

- weight inside a typed lower-exclusive and optional upper-inclusive band;
- declared EUR value strictly greater than a typed threshold.

Effects:

- assign exactly one intended department;
- require insurance approval as an independent workflow hold.

Default version 1:

| Rule identifier | Condition | Effect |
| --- | --- | --- |
| `WEIGHT-MAIL-UP-TO-1-KG` | `0 kg < weight <= 1 kg` | Mail Department |
| `WEIGHT-REGULAR-UP-TO-10-KG` | `1 kg < weight <= 10 kg` | Regular Department |
| `WEIGHT-HEAVY-OVER-10-KG` | `weight > 10 kg` | Heavy Department |
| `VALUE-INSURANCE-OVER-1000-EUR` | `declared value > EUR 1,000` | Pending insurance approval |

Country is a mandatory validated parcel fact, but version 1 does not route by
country. Optional additional attributes are defensively preserved but cannot
affect version 1 decisions.

## Deliberately deferred vocabulary

Conditions:

- destination country belongs to an allow-listed set;
- explicitly supported additional attribute equality.

Effects:

- send to manual review when no safe route is possible.

Country allow lists, arbitrary additional-attribute conditions, unrestricted
expressions, and manual-review effects are not implemented. Country remains a
required parcel fact but does not affect the default department. Any future
rule type must add typed domain validation, simulation fixtures, authorization,
and audit before activation.

## Safety invariants

- Exactly one intended department must be selected.
- Approval effects are additive and do not replace the intended department.
- All positive supported weights must be covered; zero and negative weights are
  invalid parcel facts.
- Routing bands must not overlap.
- A catch-all route must exist.
- Rules after a catch-all route are unreachable and invalid.
- Department references must exist.
- Thresholds and priorities must be valid and unambiguous.
- Stable identifiers and department-rule priorities must be unique.
- Activating a rule set must be atomic.
- Historical decisions retain their original rule-set version.

## Change workflow

1. Create a new draft version.
2. Validate schema and semantic invariants.
3. Run boundary, golden-corpus, and property tests.
4. Simulate the draft against representative and recent parcels.
5. Present a decision diff to the authorized reviewer.
6. Activate atomically with an audit record.
7. Monitor distribution and error metrics.
8. Roll back by reactivating the previous valid version if necessary.

## Administration contract

- The browser submits only three typed decimal values: Mail upper boundary,
  Regular upper boundary, and EUR insurance threshold.
- The server assigns the immutable next version and stable default rule
  identifiers.
- Draft creation runs domain semantic validation before persistence.
- Simulation is bounded to 100 representative parcels per request. The operator
  sees changed/unchanged totals and friendly before/after outcomes.
- Activation requires RuleAdministrator, commits atomically, retires the prior
  active version, and records a privacy-safe audit event.
- Rollback reactivates a prior valid version. It never edits a historical
  decision or rewrites the rule-set version stored with that decision.
- Monitoring is represented by version history, activity, and preserved
  decision explanations. Production distribution alerts require the client
  telemetry platform.

Verification:

- `Create_WhenWeightBandsContainGap_RejectsRuleSet`
- `Create_WhenWeightBandsOverlap_RejectsRuleSet`
- `Lifecycle_WhenDraftIsValid_SimulatesActivatesAndRollsBack`
- `RuleLifecycle_WhenContextsRestart_PreservesAtomicActiveVersion`
- `RuleDraft_WhenIdentityLacksAdministratorRole_ReturnsForbidden`
- `Rules_WhenAdministratorCompletesLifecycle_ActivatesAndRollsBack`
