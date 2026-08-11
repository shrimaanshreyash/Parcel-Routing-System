# Final Security Review

- Date: 2026-07-28
- Scope: ASP.NET Core API, React operator client, XML import boundary,
  PostgreSQL persistence, Nginx/Compose packaging, and production
  authentication configuration
- Outcome: no unresolved critical or high-severity finding was identified in
  the reviewed application scope

## Resolved findings

### 1. Invalid XML parcel rows could discard valid siblings

- Severity: Medium correctness and availability risk
- Risk: one malformed parcel inside an otherwise supported manifest could stop
  useful sibling rows rather than producing the documented partial result.
- Resolution: the streaming parser now classifies row-local shape and value
  errors as privacy-safe failed rows. Document safety failures still reject the
  complete upload.
- Evidence: mixed-row, invalid-country, unsupported-row, malformed-document,
  DTD/XXE, and limit fixtures plus parser, application, and API tests.

### 2. Forwarded-header trust was not explicit for public ingress

- Severity: Medium deployment-hardening risk
- Risk: public deployments need a precise trust boundary before using
  forwarded client or scheme information.
- Resolution: forwarded headers are processed before HTTPS/HSTS decisions,
  allow one hop, retain safe loopback defaults for local review, and accept
  only explicitly configured production CIDR networks. Invalid CIDR
  configuration refuses startup.
- Evidence:
  `ReverseProxy_WhenTrustedNetworkIsConfigured_UsesBoundedOptions` and
  production Compose configuration validation.

### 3. Shared integration-test rate quotas could hide unrelated behavior

- Severity: Low verification-quality risk
- Risk: the production upload ceiling correctly returned HTTP 429, but several
  unrelated fixture tests sharing one test identity could consume that quota
  and obscure the behavior under test.
- Resolution: rate limits are configuration-backed with unchanged safe
  defaults. Disposable integration hosts use explicit test ceilings, while a
  dedicated low-ceiling test proves the real HTTP 429 path.
- Evidence: all 34 API/security and operational-contract tests pass together,
  including
  `Query_WhenRateWindowIsExhausted_ReturnsTooManyRequests`.

### 4. Character-quota failures lacked their stable limit classification

- Severity: Low safe-error consistency risk
- Risk: the hardened XML reader rejected the document but exposed the generic
  invalid-manifest code instead of the stable document-limit code expected by
  operators and tests.
- Resolution: quota exceptions are mapped to the safe manifest-limit contract
  without exposing parser internals or source data.
- Evidence: the 2,000,000-character parser/API test and live HTTP 413 fixture.

## Verified controls

- Unauthenticated requests return HTTP 401 and malformed production JWTs are
  rejected.
- Operator, InsuranceApprover, and RuleAdministrator permissions are enforced
  independently by the API; denied role paths return HTTP 403.
- Production refuses Development authentication and has no silent local
  reviewer fallback.
- Upload bytes, XML characters, and row counts are bounded at 2 MiB,
  2,000,000 characters, and 10,000 rows.
- DTDs, XXE, external resolution, malformed documents, and unsupported roots
  are rejected.
- Safe Problem Details omit request bodies, stack traces, exception details,
  recipient data, and secrets.
- Nginx supplies restrictive CSP and defensive response headers.
- NuGet and npm vulnerability audits are clean, and the bounded source scan
  found placeholders/environment references only.

## Final revalidation

- No unresolved Critical or High finding was identified.
- Middleware processes forwarded headers before scheme-sensitive behavior and
  authentication before authorization:
  `src/ParcelRoutingSystem.Api/Program.cs:30`,
  `src/ParcelRoutingSystem.Api/Program.cs:46`, and
  `src/ParcelRoutingSystem.Api/Program.cs:48`.
- Development and production authentication are selected explicitly, while
  production uses JWT validation:
  `src/ParcelRoutingSystem.Api/Configuration/ParcelRoutingServiceCollectionExtensions.cs:132`
  and
  `src/ParcelRoutingSystem.Api/Configuration/ParcelRoutingServiceCollectionExtensions.cs:152`.
- Role policies are enforced server-side at
  `src/ParcelRoutingSystem.Api/Configuration/ParcelRoutingServiceCollectionExtensions.cs:185-193`.
- Upload configuration is constrained to 2 MiB, 10,000 rows, 2,000,000
  characters, and 60 seconds or less at
  `src/ParcelRoutingSystem.Api/Configuration/ParcelRoutingApiOptions.cs:107-126`.
- The XML reader prohibits DTDs and external resolution at
  `src/ParcelRoutingSystem.Infrastructure/Xml/LegacyXmlParcelManifestParser.cs:163-166`.
- The packaged web boundary sets CSP, frame protection, content-type,
  referrer, and permissions policies at `ops/nginx.conf:9-13`.
- A focused React/TypeScript source scan found no `dangerouslySetInnerHTML`,
  direct `innerHTML`, `eval`, `localStorage`, or `sessionStorage` use in the
  application source.
- Live review confirmed default active version 1 and the explicit
  Development-only Local reviewer. This is local evidence, not a production
  identity-provider deployment.

## Deployment boundaries, not application findings

- A real identity provider, browser client registration, role-claim mapping,
  TLS ingress, secret store, telemetry backend, alerting, backup policy,
  retention, and environment load targets require client infrastructure.
- Full WCAG conformance and an environment-specific load/soak benchmark are not
  claimed.
- No cloud deployment or production identity-provider integration is claimed.
