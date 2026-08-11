# Operations and reviewer workflow

No production credential or environment-specific secret belongs in this
directory. `.env.example` contains names and placeholders only.

## Prerequisites

- Docker Desktop with the Linux engine running.
- Ports `8190` and `5432` available, or override
  `PARCEL_ROUTING_WEB_PORT` / `PARCEL_ROUTING_POSTGRES_PORT`.

Confirm that `docker` resolves from the current shell before continuing.

## Start the complete reviewer stack

From the repository root:

```powershell
$env:PARCEL_ROUTING_POSTGRES_PASSWORD = '<choose-a-local-review-password>'
docker compose -f .\ops\compose.yaml up --detach --build
docker compose -f .\ops\compose.yaml ps
```

Open `http://127.0.0.1:8190/`.

The reviewer stack contains:

- PostgreSQL 17, bound to localhost;
- the .NET 10 API running as the image's non-root application user;
- the React production bundle behind unprivileged Nginx;
- same-origin `/api` and `/health` proxying;
- Development-only `Local reviewer` with Operator, InsuranceApprover, and
  RuleAdministrator roles.

The API applies reviewed migrations automatically only in Development. The
web starts after API health succeeds, and the API starts after PostgreSQL
readiness succeeds.

## Verify health and persisted state

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8190/health/live
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:8190/health/ready
Invoke-RestMethod http://127.0.0.1:8190/api/operations/overview
```

Both health endpoints must return HTTP 200.

## Stop, restart, and remove

Stop while preserving containers and database volume:

```powershell
docker compose -f .\ops\compose.yaml stop
```

Restart:

```powershell
$env:PARCEL_ROUTING_POSTGRES_PASSWORD = '<same-local-review-password>'
docker compose -f .\ops\compose.yaml up --detach
```

Remove containers while preserving the named PostgreSQL volume:

```powershell
docker compose -f .\ops\compose.yaml down
```

Do not add `--volumes` unless deleting all local review data is explicitly
intended and authorized.

## Production configuration boundary

The production overlay disables Development authentication and startup
migrations. It requires client values:

```powershell
$env:PARCEL_ROUTING_POSTGRES_PASSWORD = '<secret-from-approved-secret-store>'
$env:PARCEL_ROUTING_OIDC_AUTHORITY = 'https://<client-identity-provider>/'
$env:PARCEL_ROUTING_OIDC_AUDIENCE = '<registered-api-audience>'
$env:PARCEL_ROUTING_TRUSTED_PROXY_NETWORK = '<ingress-proxy-cidr>'

docker compose `
  -f .\ops\compose.yaml `
  -f .\ops\compose.production.yaml `
  config --quiet
```

This command validates configuration only. `PARCEL_ROUTING_TRUSTED_PROXY_NETWORK`
must be the narrow CIDR of the ingress proxy that supplies forwarded client
information; the API accepts one forwarded hop. A real deployment must also
provide TLS/ingress, browser client registration, role claims, backup policy,
observability exporter, alerts, retention, and capacity targets.

Production cannot use `Local reviewer`: the overlay forces `OidcJwt`, disables
automatic authentication, clears Development identities, and refuses missing
authority/audience values.

## Production migration step

Migrations are not applied automatically in Production. Run the reviewed
migration as a controlled release step with a short-lived connection secret:

```powershell
Set-Location <repository-root>
$env:PARCEL_ROUTING_DATABASE_CONNECTION = 'Host=<host>;Port=5432;Database=parcel_routing;Username=parcel_router;Password=<secret>'

dotnet tool restore
dotnet tool run dotnet-ef database update `
  --project .\src\ParcelRoutingSystem.Infrastructure `
  --context ParcelRoutingDbContext `
  --connection $env:PARCEL_ROUTING_DATABASE_CONNECTION

Remove-Item Env:PARCEL_ROUTING_DATABASE_CONNECTION
```

Back up the target database and review the generated migration before this
action. Readiness remains unhealthy if the API cannot reach the migrated
database.

## Local source-development alternative

For frontend/API debugging without rebuilding images, use the
source-development flow:

```powershell
$env:PARCEL_ROUTING_DATABASE_CONNECTION = 'Host=127.0.0.1;Port=5432;Database=parcel_routing;Username=parcel_router;Password=<local-password>'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'
dotnet run `
  --project .\src\ParcelRoutingSystem.Api `
  --configuration Release
```

In another PowerShell window:

```powershell
Set-Location <repository-root>\src\ParcelRoutingSystem.Web
npm ci
npm run dev
```

Open `http://127.0.0.1:8190/`. Vite forwards only `/api` and `/health`.
