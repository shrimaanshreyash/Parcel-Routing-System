# ADR 0004: Constrained Application-Owned Rules

- Status: Accepted
- Date: 2026-07-27

## Decision

Use an application-owned, typed, constrained rule model rather than a general-purpose third-party rules engine.

## Reasons

- The rule set is initially small.
- Deterministic typed conditions are easier to validate, simulate, secure, and explain.
- Arbitrary expressions would expose unnecessary execution and configuration risk.
- The implementation remains understandable for safe live modification.

## Influences

Mature engines informed priority, versioning, validation, explanation, and simulation concepts. Their runtime complexity is not justified for the initial scope.

## Revisit when

- Rule vocabulary becomes genuinely broad.
- Business users require industry-standard decision tables or DMN.
- Performance measurements show the evaluator is inadequate.
- Integration requirements mandate an established rule platform.
