# ADR 0007: Confirm duplicate manifests with privacy-safe fingerprints

- Status: Accepted
- Date: 2026-07-28

## Context

The supported legacy XML shape may omit trustworthy parcel identifiers and may
contain valid duplicate rows. Silently deduplicating rows would change source
data. Conversely, an accidental second submission should not silently create
another physical batch.

Network replay and deliberate re-import are different concerns:

- the same operation key must replay the original result;
- the same manifest submitted later under a new operation requires operator
  confirmation;
- a confirmed re-import must preserve every source row.

Raw XML and recipient data are unnecessary for detection and must not be
retained.

## Decision

Normalize the supported parcel routing facts in source order together with the
fallback-country context and hash them with SHA-256.

- Store the fingerprint on the batch.
- Check operation idempotency before fingerprint history.
- When a prior fingerprint exists, return a safe duplicate conflict containing
  only the prior batch identifier and creation time.
- Allow a separate explicit `confirmDuplicate` request to create a new batch.
- Keep valid duplicate rows inside either batch.
- Do not persist raw XML, recipient fields, or a client filename as part of the
  fingerprint.

## Consequences

- Accidental repeat submissions receive a visible guardrail.
- Network replay remains idempotent.
- Deliberate operational re-import remains possible and auditable.
- A simultaneous submission with different operation keys is still controlled
  by the UI's single in-flight action and the prior-fingerprint check; future
  multi-ingress deployments may add a short database advisory lock if measured
  concurrency requires it.
- A semantically equivalent manifest whose supported facts and fallback match
  is intentionally treated as the same manifest even if irrelevant recipient
  text or XML formatting differs.

## Verification

- `Create_WhenIdempotencyKeyRepeats_ReturnsOriginalBatch`
- `Create_WhenManifestWasImportedPreviously_RequiresConfirmation`
- `Create_WhenDuplicateIsConfirmed_CreatesNewCompleteManifest`
- `Import_WhenManifestRepeats_RequiresExplicitConfirmation`
- Real browser warning, prior-batch link, and confirmed second 17-row import
