# ADR 0001: Stack and Application Model

- Status: Accepted
- Date: 2026-07-27

## Decision

Use .NET 10 LTS with a controller-based ASP.NET Core Web API, React 19 with
TypeScript and Vite, PostgreSQL with Entity Framework Core, xUnit,
Testcontainers, browser-driven acceptance checks, and
OpenTelemetry-compatible instrumentation boundaries.

## Reasons

- Strong enterprise support without requiring excessive framework code.
- Clear domain modeling and mature testing, security, configuration, and health capabilities.
- React and TypeScript provide a clear, maintainable operator interface.
- Controller conventions keep API contracts and file-upload behaviour easy to trace during review.
- The selected technologies are common enough for a client team to take over.

## Why this stack fits this project

- C# value objects and decimal arithmetic make the 1 kg, 10 kg, and EUR 1,000
  boundaries explicit without binary floating-point surprises.
- ASP.NET Core supplies production-grade authentication, authorization,
  Problem Details, rate limiting, health checks, configuration validation,
  background services, and dependency injection without assembling many
  unrelated libraries.
- PostgreSQL supplies the transactions, constraints, indexes, and
  `FOR UPDATE SKIP LOCKED` behavior needed for immutable decisions and durable
  concurrent batch processing.
- React and TypeScript support the interactive operator, approval, history, and
  rule-lifecycle workflows while keeping routing decisions on the server.
- Controller-based endpoints make multipart XML uploads, role boundaries, and
  version-stable contracts straightforward to locate during review.

## Alternatives considered

- **Java and Spring Boot:** technically suitable and similarly enterprise-ready,
  but it would add more ceremony for this project scope and slow live
  iteration without producing a stronger routing model.
- **Node.js with a TypeScript backend:** would reduce the number of languages,
  but the selected .NET stack gives stronger domain primitives, decimal-heavy
  business modeling, background-service composition, and built-in operational
  controls for this workload.
- **Python with FastAPI:** excellent for rapid APIs, but dynamic runtime behavior
  and a less natural durable-worker/domain-model story made it weaker for this
  rules-and-reliability workload.
- **Go:** strong for compact services and concurrency, but less expressive for
  the versioned rule lifecycle and not aligned as closely with the requested
  full-stack operator experience.
- **Blazor:** would provide one language, but React keeps the browser boundary
  explicit and uses a broadly transferable frontend stack.
- **SQLite or a document database:** simpler locally, but weaker for the chosen
  concurrent row-claiming, transactional audit, and production handoff needs.
- **Microservices and a message broker:** not justified by measured scale. A
  modular monolith keeps transactions and debugging simple while preserving
  substitution points for a future external queue.

## Trade-offs

- The local machine requires .NET and container tooling setup.
- Separate frontend and backend toolchains require contract discipline.
- C# ownership must be demonstrated through explanation, debugging, and
  live-change practice rather than hidden by generated code.
