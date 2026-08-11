# Architecture Diagrams

These diagrams are deliberately maintained as Mermaid source instead of
AI-generated artwork. They stay accurate, reviewable, editable, and render
directly in GitHub Markdown.

## 1. System and deployment context

```mermaid
flowchart LR
    Operator["Parcel operator"]
    Approver["Insurance approver"]
    Administrator["Rule administrator"]
    Browser["React 19 + TypeScript operator console"]
    Proxy["Unprivileged Nginx<br/>same-origin /api and /health proxy"]
    Api["ASP.NET Core 10 API<br/>auth, authorization, limits, safe errors"]
    Worker["Hosted durable batch processor"]
    Database[("PostgreSQL 17<br/>decisions, approvals, rules,<br/>batches and audit events")]
    Identity["Client OIDC provider<br/>(production deployment boundary)"]
    Telemetry["Client telemetry and alerting<br/>(deployment boundary)"]

    Operator --> Browser
    Approver --> Browser
    Administrator --> Browser
    Browser --> Proxy
    Proxy --> Api
    Api --> Database
    Worker --> Database
    Api --> Worker
    Identity -. "JWT roles in production" .-> Api
    Api -. "structured signals" .-> Telemetry
```

Design note: the local reviewer flow replaces only the external identity
provider. It does not replace server authorization, routing, persistence, or
audit behavior.

## 2. Code dependency direction

```mermaid
flowchart TD
    Web["Web<br/>presentation and navigation"]
    Api["API<br/>HTTP contracts and security boundary"]
    Infrastructure["Infrastructure<br/>PostgreSQL and XML adapters"]
    Application["Application<br/>use cases and inward-owned ports"]
    Domain["Domain<br/>pure deterministic routing policy"]

    Web --> Api
    Api --> Application
    Api --> Infrastructure
    Infrastructure --> Application
    Infrastructure --> Domain
    Application --> Domain

    Boundary["Domain has no outward framework dependencies"]
    Boundary -.-> Domain
```

Design note: runtime collaboration and compile-time dependency direction are
different. Infrastructure depends on interfaces owned by Application; the pure
domain knows nothing about HTTP, XML, EF Core, PostgreSQL, React, or identity.

## 3. Manual parcel decision

```mermaid
sequenceDiagram
    actor Operator
    participant Web as React Web
    participant API as ASP.NET Core API
    participant App as RouteParcelUseCase
    participant Rules as Active Rule Repository
    participant Domain as RoutingRuleSet
    participant DB as PostgreSQL

    Operator->>Web: Enter weight, EUR value and country
    Web->>API: POST /api/parcels/route + idempotency key
    API->>App: RouteParcelCommand
    App->>Rules: Load active immutable version
    App->>Domain: Route validated Parcel
    Domain-->>App: Department + approval state + reasons + rule IDs
    App->>DB: Save decision and audit event atomically
    DB-->>App: Created decision or idempotent replay
    App-->>API: Explainable durable result
    API-->>Web: Version-stable response
    Web-->>Operator: Intended department and separate approval state
```

Design note: the browser never calculates a route. The same idempotency key
with changed facts is rejected rather than silently returning the wrong result.

## 4. XML import and durable processing

```mermaid
sequenceDiagram
    actor Operator
    participant Web as React Web
    participant API as Batch API
    participant Parser as Hardened XML Adapter
    participant DB as PostgreSQL
    participant Worker as Durable Processor
    participant Domain as Routing Domain

    Operator->>Web: Select XML and explicit fallback country
    Web->>API: Multipart upload
    API->>Parser: Stream supported Container document
    Parser->>Parser: Reject DTD/XXE and enforce byte/character/row/time limits
    Parser-->>API: Privacy-minimized valid and failed rows
    API->>DB: Persist batch, rows, fingerprint and audit
    API-->>Web: Accepted batch or duplicate warning
    loop Each claimable row
        Worker->>DB: Claim with token and expiring lease
        Worker->>Domain: Evaluate valid routing facts
        Domain-->>Worker: Explainable decision
        Worker->>DB: Commit decision, row state and audit atomically
    end
    Web->>API: Poll bounded batch details
    API-->>Web: Evaluated, awaiting approval and failed counts
```

Design note: a supported document isolates invalid rows. Malformed XML,
wrong roots, DTD/XXE, and document-wide limits reject the whole upload because a
safe document boundary cannot be established.

## 5. Insurance is a workflow hold

```mermaid
stateDiagram-v2
    [*] --> DecisionCreated
    DecisionCreated --> Released: value <= EUR 1,000
    DecisionCreated --> AwaitingInsurance: value > EUR 1,000
    AwaitingInsurance --> Released: authorized approval appended
    Released --> [*]

    note right of DecisionCreated
      Department is already Mail,
      Regular or Heavy.
    end note
```

Design note: insurance approval never changes the intended department and
never rewrites the historical routing decision.

## 6. Controlled rule lifecycle

```mermaid
stateDiagram-v2
    [*] --> Draft
    Draft --> Validated: gaps, overlaps and coverage pass
    Validated --> Simulated: representative decision diff reviewed
    Simulated --> Active: RuleAdministrator activates atomically
    Active --> Retired: newer version activated
    Retired --> Active: rollback reactivates prior valid version
```

Design note: rollback means reactivating an immutable prior version.
Historical decisions keep the version and explanations that originally
produced them.
