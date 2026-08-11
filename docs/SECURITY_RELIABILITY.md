# Security and Reliability Baseline

## Trust boundaries

- Public browser to web application.
- Web application to API.
- Operator-supplied parcel fields.
- Uploaded XML to parser.
- API and background processor to PostgreSQL.
- Administrator-authored rule configuration.
- Observability data leaving the application.

## Security baseline

- Production OIDC JWT access-token validation with explicit authority and audience.
- Development-only local reviewer authentication that cannot start outside Development.
- Server-side authorization policies for Operator, Insurance Approver, and Rule Administrator actions.
- No browser token persistence in local storage; if a later cookie flow is
  selected, add secure HTTP-only same-site cookies and antiforgery protection.
- HTTPS, HSTS outside development, secure response headers, and strict production CORS.
- Rate limits for authentication, routing, upload, approval, and rule-administration endpoints.
- Centralized Problem Details responses without stack traces or sensitive values.
- Secrets outside source control and validated at startup.
- Dependency, secret, and static-analysis checks in CI.

## XML and upload baseline

- Allow only the supported XML format.
- Validate filename metadata, XML media type, structure, file size, character
  count, row count, and numeric ranges.
- Reject DTD declarations.
- Disable external entities, external DTD loading, and external resource resolution.
- Use streaming parsing and bounded buffers.
- Generate server-side record identifiers; never trust the supplied filename as a path.
- Stream the request without persisting the source XML or recipient data.

## Privacy baseline

- Collect and persist only fields needed for parcel-routing operations.
- Do not include recipient names or addresses in routing metrics.
- Redact authentication, cookies, personal fields, and raw payloads from logs.
- Use identifiers rather than personal data in support and audit views.
- Never send parcel personal data to an AI system.

## Reliability baseline

- Persist the accepted batch before processing.
- Use transactions for state transitions that must be atomic.
- Make batch-row processing idempotent.
- Continue valid rows when another row is malformed.
- Resume pending work after a controlled or unexpected restart.
- Separate retryable infrastructure failures from permanent validation failures.
- Expose liveness and readiness separately.
- Fail closed to manual review if there is no valid active rule set.

## Operational signals

- Request count, duration, and failure rate.
- Routing counts by department and rule-set version.
- Insurance-hold counts and approval latency.
- Batch queue depth, processing age, row failures, and retry counts.
- Authentication failures and authorization denials.
- Rule validation, activation, and rollback events.
- Unexpected changes in department distribution.

All signals must be structured, correlated, and free of personal parcel data.

## Verified controls

- `compose.production.yaml` forces `ASPNETCORE_ENVIRONMENT=Production`,
  `OidcJwt`, an explicit authority and audience, disabled automatic
  authentication, and empty Development actors/roles.
- The reviewer Compose file binds PostgreSQL and web ports to `127.0.0.1`.
  PostgreSQL readiness gates API startup; API health gates web startup.
- The API image runs as the .NET non-root application user. The web image uses
  unprivileged Nginx.
- Nginx adds `Content-Security-Policy`, `X-Content-Type-Options`,
  `Referrer-Policy`, `Permissions-Policy`, and frame protection. Fonts are
  limited to same-origin packaged assets and `data:` assets produced by the
  reviewed frontend bundle.
- Same-origin proxying avoids permissive browser CORS configuration for the
  packaged reviewer flow.
- Forwarded headers are accepted from one proxy hop only. Loopback is the safe
  reviewer default; Production requires an explicit trusted proxy CIDR, and an
  invalid CIDR prevents startup.
- Routing, upload, approval, and query limits are configuration-backed with
  fixed safe production defaults. Tests raise only disposable-host ceilings,
  while a dedicated low-ceiling test proves HTTP 429 behavior.
- Operator, InsuranceApprover, and RuleAdministrator policies are verified
  independently by API integration tests. UI action visibility follows the
  server-provided allow-listed roles, but the API remains the authority.
- XML DTD declarations and external resolution are rejected. File bytes,
  characters, rows, and processing time are bounded; recipient data and raw XML
  are not persisted.
- Supported manifests isolate invalid parcel rows without losing valid siblings.
  Malformed XML, wrong roots, DTD/XXE attempts, unsupported document structures,
  and exceeded document limits fail the entire upload with a safe error.
- Duplicate-manifest fingerprints contain normalized routing facts and fallback
  context only. Warning responses expose only a prior batch identifier and
  timestamp.
- Approval and rule lifecycle evidence is append-only and privacy-minimized.
- API failures use safe Problem Details; duplicate imports return a deliberate
  conflict contract rather than creating a silent second batch.
- A source scan found only documented secret placeholders and environment
  references. No credential value is stored in the repository.
- NuGet and npm production/full vulnerability audits reported no known
  vulnerabilities on 28 July 2026.

## Reliability evidence

- An operation replay returns the original batch; changed input under one key is
  rejected.
- A sequential prior manifest import requires explicit operator confirmation.
- Valid duplicate rows inside a manifest are retained.
- Database row leases, `FOR UPDATE SKIP LOCKED`, and claim tokens prevent
  concurrent row ownership and recover work after expiry.
- A failed audit insert rolls back the corresponding decision transaction.
- Approval replay returns the original evidence.
- Rule activation and rollback are serializable and permit one active version.
- The named Compose volume was preserved throughout the validation run. The
  preserved 29 July review state contains 49 routing decisions, 13 remaining
  insurance holds, 4 deliberately generated import-issue rows, and 0 pending
  durable rows.
- `/health/live` and `/health/ready` returned HTTP 200 through the packaged web
  origin after restart.
- The final active policy is default version 1: Mail through 1 kg,
  Regular above 1 kg through 10 kg, Heavy above 10 kg, and insurance above
  EUR 1,000.

## Deployment boundary

The application supplies instrumentation-ready structured logging, correlation,
health endpoints, and privacy-safe operational data. It does not claim a
production OpenTelemetry collector, dashboard, alert route, retention policy,
or measured SLO because those require a client environment. The production
operator must provide:

- OIDC authority, audience, browser client registration, and role claims;
- PostgreSQL secret management and an approved migration execution step;
- TLS termination and public ingress policy;
- telemetry exporter endpoint, dashboards, alerts, and retention;
- backup, restore, capacity, and load-test targets.
