# ADR 0005: Enforce Dependency Security Gates

- Status: Accepted
- Date: 2026-07-27

## Context

The official ASP.NET Core template restored a transitive `Microsoft.OpenApi` version covered by a high-severity availability advisory. Treating warnings as errors stopped the build before application code depended on that package.

Dependency versions can change independently of source code when they are not locked, and suppressing audit warnings would hide a known operational risk.

## Decision

- Commit NuGet and npm lock files.
- Audit direct and transitive NuGet dependencies during restore.
- Treat build and audit warnings as errors.
- Run `npm audit --audit-level=high` for the frontend.
- Pin the patched transitive package directly when the owning framework package does not yet select a safe version.
- Never suppress a security advisory merely to make a build pass.
- Reassess direct pins when framework packages advance so unnecessary overrides can be removed safely.

## Consequences

- A known vulnerable dependency fails the normal engineering workflow early.
- Clean-checkout builds remain reproducible.
- Direct transitive pins require a short explanation and periodic review.
- The repository contains explicit evidence of why a package override exists.
