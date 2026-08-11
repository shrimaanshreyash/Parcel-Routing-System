# Final Verification

- Date: 2026-08-11
- Publication repository: `https://github.com/shrimaanshreyash/Parcel-Routing-System`
- Publication branch: `codex/parcel-routing-publication`
- Standard reviewer URL: `http://127.0.0.1:8190/`
- Isolated verification URL: `http://127.0.0.1:8191/`

This record covers the standalone portfolio repository. It does not claim a
public deployment, cloud environment, or production identity-provider
integration.

## .NET verification

```powershell
dotnet restore .\ParcelRoutingSystem.slnx --locked-mode
dotnet format .\ParcelRoutingSystem.slnx --no-restore --verify-no-changes
dotnet build .\ParcelRoutingSystem.slnx --configuration Release --no-restore --warnaserror
dotnet test .\ParcelRoutingSystem.slnx --configuration Release --no-build --no-restore
dotnet list .\ParcelRoutingSystem.slnx package --vulnerable --include-transitive
```

- Locked restore passed.
- Formatting verification passed.
- Release build passed with 0 warnings and 0 errors.
- All 139 automated tests passed, with 0 failures and 0 skipped:
  - Domain: 55
  - Application: 31
  - Infrastructure, PostgreSQL, and XML: 19
  - API integration and security: 34
- EF Core reported no model changes since the latest migration.
- The NuGet audit found no known vulnerable direct or transitive packages in
  any solution project.

## Frontend verification

```powershell
Set-Location .\src\ParcelRoutingSystem.Web
npm ci
npm run typecheck
npm run lint
npm run build
npm audit --omit=dev --audit-level=moderate
npm audit --audit-level=moderate
```

- Clean dependency installation, TypeScript checking, Oxlint, and the Vite
  production build passed.
- Both production-only and complete npm audits reported 0 vulnerabilities.
- The lock file includes the patched `nanoid` 3.3.18 release.
- Generated production bundle:
  - JavaScript: 330.30 kB before gzip, 96.27 kB after gzip
  - CSS: 37.98 kB before gzip, 10.50 kB after gzip

## Privacy and repository review

- High-confidence credential, private-key, access-token, email-address, local
  machine path, and personal-data scans found no publishable matches.
- The public XML corpus contains routing inputs only. Recipient names and
  addresses are discarded by the bounded parser and are not persisted.
- Both legacy `Receipient` and corrected `Recipient` XML spellings are tested.
- Private source material, internal working notes, local environment files,
  generated credentials, databases, logs, dependency folders, build output,
  browser automation output, and generated limit fixtures are excluded.
- The staged-file review found no generated, private, or runtime artifacts.

## Compose and live runtime

- Development and production-overlay Compose configuration validation passed.
  The production overlay used non-secret placeholder OIDC and trusted-proxy
  values; it was not deployed.
- Reviewer images rebuilt successfully from the current source.
- PostgreSQL 17 and the API reported healthy.
- The application root, `/health/live`, and `/health/ready` returned HTTP 200;
  both health endpoints returned `Healthy`.
- The active immutable rule set was employer-default version 1:
  - Mail through 1 kg
  - Regular above 1 kg through 10 kg
  - Heavy above 10 kg
  - Insurance approval above EUR 1,000
- The local identity was `Local reviewer` with Operator, InsuranceApprover,
  and RuleAdministrator roles.

## Browser smoke test

A fresh Playwright-controlled Chromium session opened the rebuilt reviewer
without network mocks.

- The page title and navigation used the standalone Parcel Routing System
  branding.
- Overview rendered API-connected status and persisted decision history.
- New parcel rendered labelled weight, EUR value, destination-country, and
  optional-reference controls.
- The UI stated that routing is calculated by the server, not the browser.
- The browser console contained 0 errors and 0 warnings.

The smoke test did not submit a parcel, approval, draft rule set, or import.

## Publication evidence

The curated source is published from `codex/parcel-routing-publication` to the
new public repository's `main` branch using a normal push. The final commit,
remote tree, and branch state are verified after publication and reported with
the repository handoff. No force push, tag, release, AWS deployment, or public
application deployment is part of this work.

## Honest limitations

- `Local reviewer` is a Development-only evaluation identity, not a real
  employee or production authentication system.
- Production OIDC/JWT validation and server authorization are implemented, but
  no client identity provider, gateway token flow, or deployment-specific role
  mapping is configured here.
- Structured logging, correlation, health, audit, and instrumentation
  boundaries exist, but no production telemetry collector, dashboard, alert
  route, measured SLO, or retention policy is claimed.
- Bounded XML parsing and durable processing are verified, but no
  environment-specific load or soak benchmark is claimed.
- Responsive layout, labels, focus behavior, and reduced motion were reviewed;
  full WCAG conformance is not claimed.
