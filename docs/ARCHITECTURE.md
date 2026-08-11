# Architecture

## Goal

Deliver a client-ready parcel routing application that remains small enough to understand, test, demonstrate, and modify safely.

## Chosen shape

The application is a modular monolith with separate frontend and backend deployable assets and inward-facing code dependencies.

```text
ParcelRoutingSystem.Web
        |
ParcelRoutingSystem.Api
        |
ParcelRoutingSystem.Application
       / \
ParcelRoutingSystem.Domain
        |
ParcelRoutingSystem.Infrastructure
```

The diagram shows runtime collaboration, not dependency direction. `Infrastructure` implements interfaces owned by the application or domain boundary. The domain never imports infrastructure.

## Project responsibilities

### Domain

- Parcel value objects and invariants.
- Departments, approval states, and routing decisions.
- Constrained rule conditions and effects.
- Deterministic rule evaluation.
- Rule-set semantic validation.
- No framework, database, HTTP, file, or UI dependencies.

### Application

- Route-one-parcel use case.
- Create, validate, process, and inspect batch use cases.
- Insurance approval workflow.
- Draft, simulate, activate, and roll back rule-set use cases.
- Ports for persistence, clocks, identities, audit, and telemetry.

### Infrastructure

- Entity Framework Core and PostgreSQL mappings.
- Repositories and transactions.
- Transactional audit storage and durable lease-protected batch state.
- Secure streaming XML and privacy-minimizing legacy adaptation.
- Hosted durable batch execution and bounded operational read models.
- Later adapters: telemetry exporters and external integrations.

### API

- ASP.NET Core controllers and HTTP contracts.
- Authentication and authorization policies.
- Request validation, Problem Details, rate limits, antiforgery, and health endpoints.
- Dependency registration and correctly ordered middleware.

### Web

- React operator and administrator workflows.
- Labelled forms, persisted batch history, progress, decision details, approval
  queue, activity, and constrained rule-change screens.
- No business rule implementation.

## Core decision model

A parcel decision separates destination from workflow state:

```text
RoutingDecision
- intendedDepartment
- approvalState
- matchedRuleIds
- ruleSetVersion
- reasons
- decidedAt
- correlationId
```

A high-value parcel can therefore show `Heavy Department` as its intended destination while remaining `PendingInsuranceApproval`. It is not marked routed until approval succeeds.

## Batch reliability

- The API persists batch metadata before accepting processing.
- A hosted background processor claims pending database jobs.
- Each row has a stable batch-row identity and independent status.
- Retries resume incomplete work without duplicating completed decisions.
- Process restart recovers pending or interrupted work.
- A future external queue remains an infrastructure substitution, not a domain rewrite.

## Implemented runtime boundary

The application layer owns route, approval, batch, and rule-lifecycle ports and
use cases. Infrastructure implements those ports with scoped EF Core contexts
and PostgreSQL transactions.

- Normalized SHA-256 fingerprints bind idempotency keys to complete parcel,
  batch, or rule inputs.
- Decisions and approvals are append-only records; approvals do not rewrite the
  decision that justified them.
- State changes and privacy-safe audit events commit in the same transaction.
- Rule-set activation and rollback use serializable transactions and a database
  constraint that permits only one active version.
- Batch rows use claim tokens and expiring leases. `FOR UPDATE SKIP LOCKED`
  prevents two processors from claiming the same pending row.
- A temporarily unavailable active rule set defers a row for retry instead of
  recording a permanent business failure.

ASP.NET Core composes those capabilities without moving business rules into
controllers or React:

- Controller contracts call application use cases and return version-stable
  explainable records.
- Production mode validates OIDC JWT access tokens; Development uses an explicit
  local reviewer scheme that is rejected outside Development.
- Operator, Insurance Approver, and Rule Administrator policies are enforced on
  the server, with 401 and 403 integration coverage.
- Configuration-backed cost-specific rate limits retain safe production
  defaults while allowing isolated deterministic tests. Safe Problem Details,
  correlation identifiers, security headers, liveness, and PostgreSQL readiness
  are wired in the pipeline.
- Forwarded headers are processed before HTTPS/HSTS decisions, accept one proxy
  hop, and trust only loopback defaults or explicitly configured CIDR networks.
- Raw XML is streamed through a hardened reader. DTDs and external resolution
  are prohibited; bytes, characters, rows, and time are bounded. Both
  `Receipient` and `Recipient` are accepted as boundary-only aliases, and the
  recipient subtree is discarded before application processing. Malformed
  documents fail as a document; supported documents retain valid sibling rows
  while malformed parcel rows become privacy-safe failed rows.
- A hosted worker claims durable rows with a new dependency-injection scope per
  attempt; failures remain restartable through database leases.
- The existing React operator shell now calls only same-origin `/api` routes for
  routing, approval, import progress, active rules, overview, and activity.

The operator delivery extends the same boundaries without introducing another
runtime:

- Bounded operational reads return recent batches, one decision with approval
  evidence, the awaiting-insurance queue, and constrained decision/activity
  history windows. The application fixes allowed ranges and page sizes; EF
  applies date filters, ordering, counts, and paging in PostgreSQL. Date and
  approval/date indexes keep those reads stable as retained history grows.
- Typed decision and activity categories are applied in PostgreSQL before
  counting and paging. The browser therefore cannot present a client-filtered
  subset whose page totals describe different data.
- A dedicated import-attention read model maps the Overview issue and queue
  counters to privacy-safe batch-row identifiers, stable error codes/messages,
  and current durable states. Raw XML and recipient data remain outside it.
- A normalized manifest fingerprint covers the supported routing facts plus
  fallback-country context. It does not retain raw XML or recipient data.
- An idempotency key protects one network operation. A prior fingerprint under
  another key produces a safe duplicate warning; explicit confirmation creates
  a new complete batch and preserves duplicate rows inside the source.
- Rule administration accepts only typed decimal boundaries. The domain owns
  semantic validation; the application owns version allocation, simulation,
  atomic activation, audit, and rollback.
- Insurance approval appends separate evidence. It never mutates the immutable
  decision or turns insurance into a department.
- The React shell owns navigation and presentation state only. After an
  approval, it reloads affected server read models so the queue, overview, and
  related batch views do not show stale workflow state.
- Nginx serves the production React bundle, proxies only same-origin `/api` and
  `/health` requests, and adds a restrictive content-security policy and
  defensive headers.
- Docker Compose orders PostgreSQL readiness, API health, and web startup. A
  stop/start cycle preserves review data because no destructive volume command
  is part of the reviewer workflow.

The deployment-specific OIDC provider, browser token acquisition, telemetry
exporter, and alert destinations remain client decisions. Production
configuration refuses the Development reviewer scheme rather than inventing
local password accounts.

## Deliberately excluded initially

- Microservices.
- Kubernetes.
- Redis or RabbitMQ.
- GraphQL or gRPC.
- Arbitrary expression evaluation.
- AI-generated routing decisions.
- Multiple databases.

These may be reconsidered only when a measured requirement makes their operational cost worthwhile.

Interview-ready Mermaid diagrams for the system context, dependency direction,
manual routing, durable XML processing, insurance workflow, and rule lifecycle
are maintained in
[`interview/ARCHITECTURE_DIAGRAMS.md`](interview/ARCHITECTURE_DIAGRAMS.md).
