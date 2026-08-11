# ADR 0002: Repository Boundary and Modular Monolith

- Status: Accepted
- Date: 2026-07-27

## Decision

Keep the complete project at the repository root with source, tests,
operations, and engineering documentation separated by purpose.

Build the application as a modular monolith with Domain, Application,
Infrastructure, API, and Web projects. Keep private source manifests and local
working material outside the public repository; retain only privacy-safe XML
fixtures under `tests/fixtures/xml/`.

## Reasons

- Contributors can run the solution directly from the repository root.
- The root stays navigable while source, tests, operations, and documentation
  remain explicit.
- The modular monolith provides strong boundaries without distributed-system overhead.
- A client team can navigate responsibilities and replace infrastructure without rewriting the routing domain.

## Trade-offs

- The repository contains both backend and frontend toolchains.
- Build and setup instructions must consistently use repository-relative paths.
