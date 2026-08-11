# AI Usage Log

This is the curated public record of meaningful AI-assisted work. It contains
no parcel recipient data, credentials, secrets, or private client information.
AI does not participate in runtime routing or insurance approval. Srimaan
Shreyas Vemula remains responsible for every project decision, code change,
test, and claim.

## How to read this record

Each entry states:

- the prompt or prompt record;
- what AI suggested or produced;
- what Srimaan accepted;
- what Srimaan rejected, corrected, or modified;
- how the result was verified;
- the limitation of the AI contribution.

Private working transcripts are intentionally excluded because they contain
local paths and project-development context. The meaningful prompts and human
decisions are summarized directly below.

## Exact prompt example 1 - pure routing domain and boundary tests

The development instruction included:

> Start Phase 1: Pure Routing Domain.
>
> Implement only the pure domain and its tests before progressing into
> application, API, database, XML, or frontend integration.
>
> Required tests:
>
> - Exact boundaries at 1 kg, 10 kg, and EUR 1,000.
> - Values immediately below and above every boundary.
> - Invalid zero or negative weight.
> - Invalid negative value.
> - Missing or invalid country.
> - High-value parcels in Mail, Regular, and Heavy bands.
> - Deterministic repeated evaluation.
> - Explanations and matched rule identifiers.
> - Regression coverage using a representative privacy-safe parcel corpus
>   without exposing personal data.
> - Appropriate property or invariant tests where useful.

### AI suggestion and output

- Proposed test-first exact and adjacent boundary coverage.
- Proposed typed value objects for weight, declared EUR value, country, rule
  identifiers, and rule-set versions.
- Proposed immutable constrained rules and explainable routing decisions.
- Added semantic validation for gaps, overlaps, duplicate identifiers,
  duplicate priorities, unreachable rules, and missing catch-all coverage.

### Srimaan's decisions

- Accepted the pure deterministic domain and strict dependency boundary.
- Accepted decimal arithmetic and exact threshold tests.
- Required insurance to remain a hold rather than a department.
- Required country validation without inventing country-based routing.
- Required 17 representative weight/value combinations to be covered without
  retaining recipient information.
- Rejected a general-purpose rules engine, arbitrary expressions, runtime AI
  decisions, database coupling, and browser-side routing.
- Corrected two initially suggested error-code names to the established domain
  vocabulary.

### Verification

- 55 domain tests protect boundaries, validation, determinism, explanations,
  dense invariants, and the privacy-safe reference corpus.
- The complete final test run and build result are recorded in
  [FINAL_VERIFICATION.md](../evidence/FINAL_VERIFICATION.md).

### Limitation

AI-generated test ideas did not prove correctness by themselves. Srimaan
reviewed the business boundaries, constrained the scope, corrected the error
vocabulary, and required the complete build and regression suite.

## Exact prompt example 2 - secure connected runtime

The exact user prompt was:

> Okay, cool. I have seen documents and, yeah, let's go with phase three before
> the final executable phase four starts. Let's go for the phase three and
> finish off the phase three with API connections and all other implementations
> of XML runtime, integration authorization, batch process for stream shift,
> and front-end integration. So once that, I'll test it, then we'll have again
> a review DB chat again once again after this. So yeah, go for the phase three.

### AI suggestion and output

- Connected the application use cases to ASP.NET Core controllers and
  PostgreSQL repositories.
- Added Development reviewer authentication, production OIDC/JWT validation,
  server-side role policies, rate limits, safe Problem Details, correlation
  identifiers, security headers, and health checks.
- Added hardened streaming XML parsing, explicit country provenance, and the
  durable hosted row processor.
- Connected React to real same-origin API contracts without copying routing
  thresholds into TypeScript.

### Srimaan's decisions

- Accepted the secure API, XML, worker, persistence, and frontend integration.
- Required the local reviewer to remain Development-only.
- Rejected first-party password accounts, fake production login, browser token
  storage, arbitrary rule execution, recipient persistence, and a message
  broker without measured need.
- Corrected record-validation metadata after real HTTP testing showed that the
  initial placement was ignored.
- Corrected asynchronous route-location generation after the real API returned
  an invalid action location.

### Verification

- Real PostgreSQL/API tests cover liveness, readiness, authentication,
  authorization, explainable routing, security headers, XML rejection, and the
  privacy-safe reference corpus through the durable worker.
- A real browser exercised manual routing, approval, XML import, rules,
  overview, and activity.
- The final combined results are recorded in
  [FINAL_VERIFICATION.md](../evidence/FINAL_VERIFICATION.md).

### Limitation

The tests prove the application security boundary, not a deployed client
identity provider. The client must still supply its OIDC tenant, browser or
gateway token flow, role mapping, TLS ingress, and secret management.

## Representative delivery and security review

### AI suggestion and output

- Added durable import restoration, privacy-safe duplicate confirmation,
  insurance approval evidence, controlled rule drafts, simulation, activation,
  rollback, and bounded operational history.
- Added reviewer and production Compose boundaries, same-origin Nginx,
  non-root containers, CSP, safe proxy trust, adversarial XML fixtures, and
  deterministic security tests.
- Proposed documentation and browser verification for the complete operator
  workflow.

### Srimaan's decisions

- Accepted only features tied to a concrete correctness, security, reliability,
  or operator problem.
- Found and required fixes for stale approval state, hardcoded active-rule
  presentation, unclear import recovery, local-reviewer identity claims,
  corrected-manifest counting, and recipient spelling compatibility.
- Rejected AWS deployment, public-hosting claims, arbitrary rules, fake
  external integrations, raw XML retention, fabricated monitoring, and full
  accessibility claims without evidence.
- Required default rule version 1 to be restored before final verification.

### Verification

- Named regression tests cover duplicate confirmation, approval roles,
  rule-lifecycle roles, pagination/filtering, upload limits, malformed JWT,
  production refusal of Development authentication, rate limiting, headers,
  safe errors, and trusted-proxy configuration.
- Compose, health endpoints, dependency audits, source scans, and packaged
  browser behavior are included in the final gate.

### Limitation

AI review can identify likely risks but cannot substitute for live state,
database, authorization, browser, or deployment-boundary verification. Every
accepted suggestion was checked at its owning runtime boundary.

## Recipient spelling and privacy correction

### AI suggestion and output

- Added `Recipient` as a narrow alias beside the source format's legacy
  `Receipient` element.
- Added parser and real API-to-worker regression coverage.
- Updated requirements and privacy documentation.

### Srimaan's decisions

- Identified the compatibility gap.
- Accepted exactly two allow-listed spellings.
- Required both recipient subtrees to be discarded before application or
  domain processing.
- Rejected rewriting source XML, accepting arbitrary elements, or
  retaining names and addresses without a business requirement.

### Verification

- Both aliases pass the parser boundary.
- The corrected spelling passes the real HTTP, PostgreSQL, and durable-worker
  path.
- The final automated run confirms the current total.

### Limitation

Recipient processing remains intentionally unsupported. A future address-based
feature would need a separate business rule, privacy review, minimum-data
decision, and new tests.

## Overall human ownership

AI accelerated comparison, implementation, test design, review, and
documentation. Srimaan:

- selected the stack and modular-monolith boundary;
- defined the country and privacy policy;
- separated department routing from insurance approval;
- constrained rule editing and rejected speculative infrastructure;
- discovered operator, recovery, identity, and compatibility defects;
- reviewed every accepted change;
- required real PostgreSQL, API, security, Compose, and browser verification;
- retained honest limitations instead of presenting planned client
  infrastructure as complete.

No personal parcel data, secret, credential, or raw source XML content was
sent to an AI system as part of the recorded implementation work.
