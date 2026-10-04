# ZiApp API

ASP.NET Core backend for the Zorjd Investments application.

## Foundation

- .NET 10 and ASP.NET Core controllers
- PostgreSQL 18 through Entity Framework Core and Npgsql
- Swagger/OpenAPI in Development and Testing
- Repository-local EF Core migration tooling
- Unit tests plus PostgreSQL integration tests
- Multi-stage, non-root Docker image
- ASP.NET Core Identity with admin-controlled account provisioning

## Repository layout

```text
src/
  ZiApp.Api/             HTTP endpoints and application startup
  ZiApp.Application/     use cases and application boundaries
  ZiApp.Domain/          investment and tax domain model
  ZiApp.Infrastructure/  PostgreSQL and external integrations
tests/
  ZiApp.UnitTests/
  ZiApp.IntegrationTests/
```

The backend starts as a modular monolith. Domain and Application remain independent
of ASP.NET Core, PostgreSQL, and other infrastructure details.

## Local development

Prerequisites:

- a .NET SDK compatible with `global.json`
- Docker Desktop
- the sibling `zi-app-infra` repository

Start PostgreSQL from the infrastructure repository:

```powershell
Copy-Item .env.example .env
docker compose -f compose.dev.yml up -d postgres
```

Then, from this repository:

```powershell
dotnet tool restore
dotnet restore --locked-mode
dotnet ef database update --project src/ZiApp.Infrastructure --startup-project src/ZiApp.Api
dotnet run --project src/ZiApp.Api --urls http://localhost:5050
```

Useful endpoints:

- `GET /health/live` confirms that the API process is running
- `GET /health` confirms that PostgreSQL is reachable
- `GET /swagger` opens interactive API documentation

## Authentication

Accounts are created only by a super administrator; there is no public registration
endpoint. See [the authentication guide](docs/security/authentication.md) for the
first-admin setup, cookie and CSRF flow, and security defaults.

## Portfolios

Signed-in users can create, list, retrieve, rename, archive, and restore their own
portfolios. These endpoints include CSRF protection and account ownership checks.
See the [portfolio guide](docs/portfolios/portfolio-management.md) for the contract,
archive policy, and acceptance checks. This stage uses the existing migrations;
there is no new database migration to apply for portfolio management.

## Instruments and manual trades

The [trade-entry guide](docs/trading/manual-trade-entry.md) documents catalog search,
admin-only catalog additions, owner-scoped buys/sells, fees, audited corrections,
decimal-string inputs, and initially pending exchange rates. Trades are not
tax-ready yet; the rate-resolution workflow is available below.

Apply `20260920125916_AddManualTradeEntry` using the procedure below before using
these endpoints. It preserves existing data; no reset is required. Correction
history and pending-rate trades prevent an unsafe downgrade.

## NBU exchange rates

The [NBU guide](docs/exchange-rates/nbu-exchange-rates.md) covers exact-date USD/UAH
fetching and caching, source-response provenance, and owner-scoped trade resolution.
Use the broker's recorded calendar date without timezone conversion, including
older records. Missing official rates stay pending; there is no weekend fallback.
Rates are decimal JSON strings, and resolving a rate does not mean filing readiness.

Apply `20260920191527_AddNbuExchangeRates` after `AddManualTradeEntry`; no database
reset is required. The API needs outbound HTTPS access to `bank.gov.ua`, without
an API key. GET reads cached data/status; CSRF-protected POST fetches/resolves:

- `GET /api/exchange-rates/usd/{date}`
- `POST /api/exchange-rates/usd/{date}/fetch`
- `GET /api/portfolios/{portfolioId}/trades/{tradeId}/exchange-rate`
- `POST /api/portfolios/{portfolioId}/trades/{tradeId}/exchange-rate/resolve`

No background rate import, cache overwrite, or automatic trade backfill is enabled.

## Splits and holdings

The [split/holdings guide](docs/holdings/splits-and-holdings.md) documents:

- Admin-only shared split creation and audited corrections at
  `/api/instruments/{instrumentId}/splits`.
- Owner-only `GET /api/portfolios/{portfolioId}/holdings`, with optional `asOf`,
  quantities, remaining FIFO lots, realized matches and USD/UAH totals.
- Explicit PendingRates results instead of incomplete financial totals.

Apply `20261002125349_AddSplitManagement` after `AddNbuExchangeRates`; existing
data are preserved. Stop older API writers before upgrading because this stage
introduces shared/exclusive ledger locking for trade/split coordination.
Split corrections preserve original inputs and saved reports. Historical cutoffs
use current revisions, not the data known at that date. These calculations use
`fifo-uah-v2-remaining-cost` and are not saved or filing-ready tax reports.

## Saved draft tax reports

The [report guide](docs/reports/draft-tax-reports.md) describes saved reports for
one portfolio and broker calendar year, including prior-year FIFO lot consumption,
full-precision USD/UAH results and the exact source inputs/rates/splits used.

- `POST /api/portfolios/{portfolioId}/tax-reports` with `{ "taxYear": 2025 }`
  saves a draft; requires the portfolio owner's active session and CSRF token.
- GET the same collection for history, or `/{id}` for a saved report.
- GET `/{id}/current-status` to detect changed inputs without updating the report.
- GET `/{id}/export?format=csv` or `json` to download saved results.

Resolve all included trade rates first. Corrections never change existing reports;
generate another draft to capture revised data. All reports have `isTaxReady: false`:
official forms, taxes payable and final filing rounding are not included.

Apply `20261002140835_AddDraftTaxReports` after `AddSplitManagement`. The migration
preserves existing data and widens calculated match precision. Stop/drain old API
writers and back up the confirmed target before applying it. No reset is required.
CSV financial columns should be imported as text to prevent spreadsheet precision loss.

## Annual preparation drafts and filing-readiness limits

The [2025 review](docs/reports/ua-2025-filing-readiness.md) records official-source
findings and open legal, FX, fee, loss, rounding and form-version questions.
The separate [annual preparation API](docs/reports/annual-preparation-drafts.md)
is implemented for 2025. POST `/api/annual-summaries` with explicit owned report
IDs, reviewed coverage/overlap confirmations, external inputs and prior-loss
claims. GET history, `/{id}`, `/{id}/export` (JSON) and `/{id}/current-status`.
Existing one-portfolio reports remain unchanged. Both workflows are drafts:
no official tax payable, accepted loss deduction or filing form is implemented.

Apply `20261003152645_AddAnnualPreparationDrafts` after `AddDraftTaxReports`
before using these endpoints. It adds a separate snapshot table without changing
existing data; downgrade protects saved drafts. Your local database is not
automatically migrated by application startup or the automated test suite.

The [sample audit](docs/domain/spreadsheet-sample-audit.md) records eight read-only
spreadsheets and the confirmed BXMT correction. Personal sample files are not
repository/CI dependencies. The annual stage includes transcribed numerical
BND/BNDX/BXMT regressions without modifying or importing the workbooks.

## Development handoff

For current development state and the exact next stage, read [AGENTS.md](AGENTS.md).

## Configurable yearly tax reports

The [configured-report guide](docs/reports/configured-tax-reports.md) documents
private yearly rates and saved tax/loss reports based on the existing FIFO drafts.
For example, enter income 18%, military 5% and dividend 9% for a chosen year.
These are your settings, not automatic legal defaults; historical years keep
their own rates. Dividend calculations are deferred; the rate is stored only.

1. POST `/api/tax-settings/{year}` with the three percentage strings.
2. Save a portfolio draft for that year using `/api/portfolios/{id}/tax-reports`.
3. POST `/api/portfolios/{id}/configured-tax-reports` with its `sourceReportId`
   and the returned `settingsId`; read/export the saved JSON or CSV.

A net loss of -10000 UAH remains visible as `lossUah: "-10000"`, with both
taxes and total `"0.00"`. On positive net profit, each tax is rounded to two
places; source FIFO values stay unchanged. Rate edits append revisions and never
rewrite old reports. Official filing forms are not part of this workflow.

Apply `20261004144649_AddConfiguredTaxReports` after `AddAnnualPreparationDrafts`.
No existing data reset is needed. This task did not migrate your local database.
There is currently no frontend settings/report screen; use Swagger/API until
the web implementation reaches this workflow. The [web foundation](../zi-app-web/README.md)
now runs on localhost:5173 and proxies API/health requests to port 5050.
It has an overview, three languages and explicit connection checks, not login
or investment-data screens. Authentication UI with real cookie/CSRF testing is next.

## Tests

```powershell
dotnet test ZiApp.sln --configuration Release
```

Integration tests create a temporary PostgreSQL container and apply real migrations.
NBU tests use fixed HTTP responses; they do not depend on live external rates.

## Apply pending migrations

Run this after pulling changes that include new migrations. API startup does not
apply migrations automatically.

Start PostgreSQL as described in [Local development](#local-development), then run
the following commands from the `zi-app-api` repository root:

```powershell
dotnet tool restore
dotnet restore --locked-mode
```

The migration tool defaults to the local database at `localhost:5432` with database
and username `zi_app` and password `zi_app_local_dev`. If your database settings
differ, set `ConnectionStrings__Database` in the same PowerShell session before
running the EF commands. Replace the placeholders with your database settings:

```powershell
$env:ConnectionStrings__Database = "Host=<host>;Port=<port>;Database=<database>;Username=<username>;Password=<password>"
```

Confirm the connection targets the intended database, then list migrations:

```powershell
dotnet ef migrations list `
  --project src/ZiApp.Infrastructure `
  --startup-project src/ZiApp.Api
```

Migrations marked `(Pending)` have not been applied to that database. Apply all
pending migrations in order:

```powershell
dotnet ef database update `
  --project src/ZiApp.Infrastructure `
  --startup-project src/ZiApp.Api
```

EF Core tracks applied migrations in `__EFMigrationsHistory` and skips them on
subsequent runs. If the database is already up to date, this command makes no
schema changes. Run `dotnet ef migrations list` again with the same project
options to verify that no migrations remain marked `(Pending)`.

## Add a migration

```powershell
dotnet ef migrations add MigrationName `
  --project src/ZiApp.Infrastructure `
  --startup-project src/ZiApp.Api `
  --output-dir Persistence/Migrations
```

## Build the API image

```powershell
docker build --tag zi-app-api:dev .
```
