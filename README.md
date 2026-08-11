# Parcel Routing System

Parcel Routing System is a full-stack operations application for deterministic
parcel routing, privacy-safe XML imports, insurance approvals, and versioned
rule management.

I built this project around one core idea: routing decisions should be easy to
explain, safe to repeat, and traceable long after they are made. The browser is
an operator interface; all routing, authorization, validation, persistence, and
workflow decisions are enforced by the server.

![Parcel Routing System overview](docs/images/parcel-routing-overview.png)

## What the application does

- Routes individual parcels to Mail, Regular, or Heavy departments.
- Separates department routing from high-value insurance approval.
- Imports legacy XML manifests through a bounded streaming parser.
- Continues processing valid rows when another row is invalid.
- Detects repeated manifests without deleting legitimate duplicate parcels.
- Restores durable batch progress after an application restart.
- Records matched rules, rule-set versions, explanations, and approval events.
- Supports controlled rule drafting, validation, simulation, activation, and
  rollback.
- Provides an operations dashboard, approval queue, batch history, and activity
  history through a React interface.
- Uses server-side roles for operator, insurance approver, and rule
  administrator actions.

## Routing rules

The application starts with immutable default rule-set version 1:

| Condition | Result |
| --- | --- |
| Weight up to and including 1 kg | Mail |
| Weight above 1 kg and up to and including 10 kg | Regular |
| Weight above 10 kg | Heavy |
| Declared value above EUR 1,000 | Insurance approval required |

Exactly EUR 1,000 does not require insurance approval. Insurance is a workflow
hold and never replaces the parcel's routing department. Destination country
is required as an ISO code but does not change these default weight rules.

## Privacy-safe XML processing

The XML adapter accepts both the legacy `Receipient` element and the corrected
`Recipient` spelling. Recipient names and addresses are discarded at the
parser boundary. They are not returned to the application layer, persisted,
logged, displayed, or used for deduplication.

DTD processing and external entities are disabled. The import path also limits
request size, XML characters, row count, numeric ranges, and processing time.
Nine public fixtures cover valid boundaries, variations, row-level failures,
invalid countries, malformed XML, unsupported structures, XXE attempts,
duplicate retries, and the complete privacy-safe decision corpus.

## Architecture

```text
React operator console
        |
Unprivileged Nginx same-origin proxy
        |
ASP.NET Core API
        |
Application use cases
       / \
Pure routing domain   Infrastructure adapters
                            |
                       PostgreSQL
```

The codebase is a modular monolith with inward-pointing dependencies. The pure
domain does not depend on ASP.NET Core, EF Core, PostgreSQL, React, XML, or
network infrastructure. Routing stays deterministic and can be tested without
starting the API or database.

### Technology

- .NET 10, ASP.NET Core, EF Core, and PostgreSQL 17
- React 19, TypeScript, Vite, and unprivileged Nginx
- xUnit unit and integration tests
- Docker Compose for the complete local environment
- OIDC/JWT authentication boundary for production configuration

## Quick start with Docker

This is the recommended way to run the complete application.

### Prerequisites

- Git
- Docker Desktop using Linux containers
- Free local ports `8190` and `5432`

### 1. Clone the repository

```powershell
git clone https://github.com/shrimaanshreyash/Parcel-Routing-System.git
Set-Location .\Parcel-Routing-System
```

### 2. Choose a local database password and start everything

```powershell
$env:PARCEL_ROUTING_POSTGRES_PASSWORD = '<choose-a-local-password>'
docker compose -f .\ops\compose.yaml up --detach --build
docker compose -f .\ops\compose.yaml ps
```

The first build downloads the required .NET, Node, Nginx, and PostgreSQL images.
Wait until both `postgres` and `api` report healthy.

### 3. Open the application

Open [http://127.0.0.1:8190](http://127.0.0.1:8190).

The local environment automatically signs in as `Local reviewer` with the
Operator, InsuranceApprover, and RuleAdministrator roles. It does not require a
username or password and is enabled only in Development.

Verify the API separately if needed:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8190/health/live
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8190/health/ready
```

Both endpoints should return HTTP 200 with `Healthy` in the response body.

### 4. Try the main workflows

1. Use **New parcel** to enter weight, declared value, and destination country.
2. Import `tests/fixtures/xml/01-valid-boundaries.xml` from the batch-import
   screen.
3. Open **Approvals** to review parcels held for insurance approval.
4. Open **Rules** to inspect active version 1 or simulate a controlled draft.
5. Use **Activity** and the overview to inspect durable decision history.

### 5. Stop the application

Stop containers while preserving the database volume:

```powershell
docker compose -f .\ops\compose.yaml down
```

To remove the disposable local database as well:

```powershell
docker compose -f .\ops\compose.yaml down --volumes --remove-orphans
```

Removing the volume permanently deletes local parcel, batch, approval, rule,
and activity data.

### Port conflicts

Override either host port before starting Compose:

```powershell
$env:PARCEL_ROUTING_WEB_PORT = '8191'
$env:PARCEL_ROUTING_POSTGRES_PORT = '55432'
$env:PARCEL_ROUTING_POSTGRES_PASSWORD = '<choose-a-local-password>'
docker compose -f .\ops\compose.yaml up --detach --build
```

The application will then be available at `http://127.0.0.1:8191`.

## Work with the source code

Install these tools when running outside containers:

- .NET SDK `10.0.302` or a compatible .NET 10 patch release
- Node.js 22.12 or newer with npm
- Docker Desktop for PostgreSQL and the integration-test containers

Start only PostgreSQL:

```powershell
$env:PARCEL_ROUTING_POSTGRES_PASSWORD = '<choose-a-local-password>'
docker compose -f .\ops\compose.yaml up --detach postgres
```

Start the API from the repository root:

```powershell
$env:PARCEL_ROUTING_DATABASE_CONNECTION = 'Host=127.0.0.1;Port=5432;Database=parcel_routing;Username=parcel_router;Password=<choose-a-local-password>'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'

dotnet restore .\ParcelRoutingSystem.slnx --locked-mode
dotnet run --project .\src\ParcelRoutingSystem.Api
```

In a second PowerShell window, start the frontend:

```powershell
Set-Location .\src\ParcelRoutingSystem.Web
npm ci
npm run dev
```

Open `http://127.0.0.1:8190`. Vite proxies only `/api` and `/health` to the
local API at port `5080`.

## Verification

Run the .NET gate from the repository root:

```powershell
dotnet restore .\ParcelRoutingSystem.slnx --locked-mode
dotnet format .\ParcelRoutingSystem.slnx --no-restore --verify-no-changes
dotnet build .\ParcelRoutingSystem.slnx `
  --configuration Release --no-restore --warnaserror
dotnet test .\ParcelRoutingSystem.slnx `
  --configuration Release --no-build --no-restore
dotnet list .\ParcelRoutingSystem.slnx package `
  --vulnerable --include-transitive
```

Run the frontend gate:

```powershell
Set-Location .\src\ParcelRoutingSystem.Web
npm ci
npm run typecheck
npm run lint
npm run build
npm audit --omit=dev --audit-level=moderate
npm audit --audit-level=moderate
```

The verified baseline is 139 automated tests:

- Domain: 55
- Application: 31
- Infrastructure, PostgreSQL, and XML: 19
- API integration and security: 34

See [Final Verification](docs/evidence/FINAL_VERIFICATION.md) for the complete
record and honest limitations.

## Repository structure

```text
.
|-- docs/                       Architecture, requirements, ADRs and evidence
|-- ops/                        Docker Compose and operations guidance
|-- src/
|   |-- ParcelRoutingSystem.Api/
|   |-- ParcelRoutingSystem.Application/
|   |-- ParcelRoutingSystem.Domain/
|   |-- ParcelRoutingSystem.Infrastructure/
|   `-- ParcelRoutingSystem.Web/
|-- tests/                      Unit, integration and XML fixture coverage
`-- ParcelRoutingSystem.slnx
```

## Security and deployment boundary

The packaged local workflow uses the Development-only `Local reviewer`
identity so every workflow can be evaluated without creating accounts.
Production mode disables that identity and requires an external OIDC/JWT
authority, audience, token flow, and role mapping.

This repository contains the production authentication and authorization
boundary, but it does not claim a deployed identity provider, cloud deployment,
production telemetry service, full WCAG conformance, or environment-specific
load and soak results.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Architecture diagrams](docs/interview/ARCHITECTURE_DIAGRAMS.md)
- [Requirement traceability](docs/REQUIREMENTS.md)
- [Routing-rule design](docs/RULE_SYSTEM.md)
- [Accepted architecture decisions](docs/adr/)
- [Security and reliability baseline](docs/SECURITY_RELIABILITY.md)
- [Security review](docs/SECURITY_REVIEW.md)
- [UX guidelines](docs/UX_GUIDELINES.md)
- [Operations guide](ops/README.md)
- [Demo walkthrough](docs/interview/DEMO_WALKTHROUGH.md)
- [Final verification](docs/evidence/FINAL_VERIFICATION.md)
- [AI usage record](docs/ai/AI_USAGE_LOG.md)
