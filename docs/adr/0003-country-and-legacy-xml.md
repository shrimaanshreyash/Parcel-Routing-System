# ADR 0003: Missing Country and Legacy XML Compatibility

- Status: Accepted
- Date: 2026-07-27

## Context

Routing requires destination country, but the supported legacy XML may omit a
country element. The legacy contract also spells recipient as `Receipient`.

## Decision

- Manual entry requires country selection.
- Batch rows may provide country directly.
- If rows omit country, the operator must select a manifest-level country before processing.
- Store whether country came from the row or manifest metadata.
- Never infer country silently from a city, postal code, or filename.
- Accept both the legacy `Receipient` spelling and the correct
  `Recipient` spelling only in the XML adapter.
- Discard either recipient subtree after structural allow-listing because names
  and addresses are not routing inputs and retaining them would add unnecessary
  personal-data risk.

## Reasons

- Explicit provenance prevents incorrect business decisions.
- Existing legacy files remain usable.
- Legacy compatibility does not contaminate the domain language.
- Supporting the corrected alias improves source compatibility without
  expanding the application or domain contract.

## Trade-offs

- Batch upload contains one additional operator step.
- Mixed-country manifests without row countries must be rejected instead of guessed.
