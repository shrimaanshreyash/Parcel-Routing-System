# Requirement Traceability

Status values are `Verified`, `Verified boundary`, or `Deferred`. `Verified
boundary` means the application-side control is complete but the final client
deployment needs external identity or observability infrastructure.

| Requirement | Delivered outcome | Primary verification | Status |
| --- | --- | --- | --- |
| Single parcel routing | Weight, EUR value, and country produce one intended department and an independent approval state | `Route_WhenParcelIsValid_ReturnsExplainableDecision`; low- and high-value live browser routes | Verified |
| Up to 1 kg | Mail Department, inclusive at 1 kg | `Route_WhenWeightIsExactlyOneKilogram_SelectsMailDepartment` and adjacent-value tests | Verified |
| Up to 10 kg | Regular Department, inclusive at 10 kg | `Route_WhenWeightIsExactlyTenKilograms_SelectsRegularDepartment` and adjacent-value tests | Verified |
| Over 10 kg | Heavy Department | `Route_WhenWeightIsImmediatelyAboveTenKilograms_SelectsHeavyDepartment` | Verified |
| Value over EUR 1,000 | Insurance hold before physical routing; exactly EUR 1,000 is not held | exact, adjacent, and dense-value tests | Verified |
| Destination country | Required ISO country; manifest fallback is explicit and persisted with provenance | country validation tests; fixture API and browser import | Verified |
| Adaptable rules | Typed immutable versions with stable identifiers; historical decisions remain unchanged | domain safety tests and PostgreSQL lifecycle test | Verified |
| Safe rule evolution | Role-gated Draft -> Validate -> Simulate -> Activate -> Monitor -> Roll back | `Rules_WhenAdministratorCompletesLifecycle_ActivatesAndRollsBack`; live six-sample simulation and rollback | Verified |
| Manual UI | Labelled, plain-language operator form and explainable decision drawer | Chrome acceptance at 1024, 1280, and 1440 CSS pixels; zero final console errors | Verified; full WCAG audit not claimed |
| Batch input | Secure XML upload using the supported legacy manifest shape and explicit fallback country | parser/security/API tests and privacy-safe browser import | Verified |
| Large files | Upload bytes, XML characters, rows, and parse time are bounded; parsing streams; rows execute durably and independently | named privacy-safe fixtures and API tests prove 2 MiB, 2,000,000-character, and 10,000-row limits; durable lease/restart evidence proves recovery | Verified for configured limits; load benchmark deferred |
| Import recovery | Bounded recent batches restore persisted details after navigation and refresh | bounded API read model and live browser restoration | Verified |
| Duplicate imports | Fingerprint plus fallback context warns; explicit re-import preserves every source row; operation replay is idempotent | `Import_WhenManifestRepeats_RequiresExplicitConfirmation` and application duplicate tests | Verified |
| Insurance workflow | Bounded queue, explainable detail, role-gated idempotent approval, append-only evidence, immediate refresh | `Approval_WhenAuthorized_LeavesDurableEvidenceAndQueueRefreshes` and browser queue refresh | Verified |
| Clear results | Friendly department, EUR value, country name/code, approval badge, rule version, and reasons | API integration plus Overview, Import, Insurance, and drawer browser acceptance | Verified |
| Historical visibility | All-time total plus server-filtered Recent 10, 24-hour, 7-day, 30-day, 12-month, and all-time decision/activity views; decision department/approval and activity event-family filters execute before bounded paging; longer histories and insurance holds use 15-item pages | `OperationsQueryUseCaseTests`, `Overview_WhenAllTimeRange_ReturnsBoundedPageMetadata`, `Overview_WhenHeavyFilterSelected_ReturnsOnlyHeavyDecisions`, `Activity_WhenImportsCategorySelected_ReturnsOnlyBatchEvents`, and packaged Chrome acceptance | Verified |
| Operational attention | Every Overview KPI has a concrete destination; Import XML separates new work from operations/history; import issues expose privacy-safe row/code/message/recovery evidence, queue rows expose pending/processing state, and both open the exact durable batch | `ImportAttention_WhenMixedRowsFail_ReturnsExactSafeIssueRows`, `Create_WhenDeclaredValueIsNegative_PreservesPlainSafeMessage`, application attention-boundary test, API contract checks, and packaged Chrome acceptance | Verified |
| Automated QA | Unit, invariant, PostgreSQL integration, parser-security, API authorization/idempotency, build, and browser checks | 55 domain + 31 application + 19 infrastructure + 34 API tests; frontend typecheck/lint/build | Verified locally |
| Safe new rule | Worked rule change is demonstrated through application/API/UI lifecycle | lifecycle tests and browser version 2 activation/rollback | Verified |
| Correctness beyond tests | Behavior reconciliation, real persisted output checks, simulation, auditability, and manual acceptance | `FINAL_VERIFICATION.md` and `DEMO_WALKTHROUGH.md` | Verified |
| Monitoring | Structured correlated privacy-safe logs, health/readiness, and instrumentation boundaries | live health checks, activity records, and configuration review | Verified boundary; exporter, dashboards, and production alerts require client infrastructure |
| Reliability | Idempotency, duplicate guard, durable jobs, row isolation, leases, safe retries, and fail-closed policy access | concurrency, rollback, restart, duplicate, and live Compose restart evidence | Verified |
| Public security | OIDC/JWT production boundary, independent server roles, rate limits, CSP/security headers, bounded trusted-proxy handling, safe uploads/errors, and no Development fallback | named 401/403/JWT/role/rate/upload/header/ProblemDetails/startup/proxy tests, production Compose validation, XML security tests, and dependency/secret audits | Verified boundary; real identity-provider deployment requires client credentials |
| Debugging readiness | Pure routing core, correlation IDs, progressive technical detail, and reproducible regressions | named tests, activity details, and demo guide | Verified |
| AI use | Honest, reviewable AI-assisted development record with human decisions and limitations | AI usage log | Verified |
| README and presentation | Architecture, trade-offs, operations guide, AI usage, and 10-15 minute demo | README, Compose restart, final verification, and walkthrough | Verified |

## Source-data decisions

- The supported legacy format may omit destination country.
- Import therefore requires an explicit ISO fallback country when a row does
  not contain one.
- Country provenance records whether the row or operator fallback supplied the
  value; the system never infers country.
- The legacy `Receipient` element and correctly spelled `Recipient` alias are
  accepted only at the XML boundary and discarded because recipient data is
  not required for a routing decision.
- Duplicate rows inside one manifest are preserved.
- A normalized manifest fingerprint plus fallback context warns about a prior
  import without storing raw XML or recipient data. Deliberate confirmation
  creates another complete batch.

## Honest deployment boundary

- Development-only `Local reviewer` authentication supports evaluator testing.
- Production configuration requires OIDC/JWT authority and audience values and
  cannot silently fall back to Development authentication.
- Browser token acquisition, a real identity provider, telemetry exporters,
  alert destinations, and load-test targets require client deployment
  infrastructure. They are documented boundaries, not fabricated completions.
