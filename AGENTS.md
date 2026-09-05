# ZiApp API: agent guide

## Purpose and repository boundaries

ZiApp is a multi-user investment tracker for USD stocks and ETFs, with multiple
portfolios per account, mandatory FIFO matching, and Ukrainian tax reporting
based on transaction-date NBU UAH exchange rates. The product must support
English, Ukrainian, and Russian.

This repository owns the ASP.NET Core API, application use cases, domain rules,
EF Core migrations, integrations, backend tests, and API Dockerfile.
The sibling [web guide](../zi-app-web/AGENTS.md) covers the React client;
the [infrastructure guide](../zi-app-infra/AGENTS.md) covers environments and deployment.
Keep all three as separate Git repositories.

## Working agreement

- Read this guide, the relevant design documents, and current code before changes.
  Inspect Git status and preserve unrelated work.
- Develop one testable stage at a time. The roadmap records direction; it does
  not authorize implementing every remaining stage in a single task.
- Keep LF line endings and follow `.editorconfig` and `.gitattributes`.
- Use the SDK selected by `global.json`, repository-local EF tooling, and package
  lock files. Do not change versions or relax analyzers just to bypass a failure.
- Keep the user's local HTTP port `5050` and HTTPS port `5051`.
- Preserve the explicit folder and `Compile Visible` entries used by Visual
  Studio. For new feature folders, follow the existing project convention and
  check file inclusion rather than assuming Solution Explorer is collapsed.
- Keep real credentials and bootstrap passwords out of tracked files and logs.
- Commit or push only when requested; verify and commit each repository separately.
- At the end of a stage, update this guide's status and relevant detailed docs.
  Record checks actually run and any remaining limitations. A completed code
  stage does not establish which migrations are applied to the user's database.

## Architecture and invariants

- Keep the modular monolith: Domain contains business rules; Application defines
  use cases and boundaries; Infrastructure implements persistence and integrations;
  Api handles HTTP, authentication/authorization, and composition.
  Domain and Application must stay independent of ASP.NET Core and EF Core.
- Generate new entity UUIDs with `Guid.CreateVersion7()` and store them as native
  PostgreSQL `uuid`. Keep existing IDs, deterministic seed IDs, fixed test
  fixtures, and Identity security/concurrency stamps intact. UUIDv7 generation
  alone needs no schema migration and is not an authorization mechanism.
- Only a super administrator provisions accounts. There is no public signup.
  Use the existing Identity cookie and CSRF flow; do not replace it casually.
- Authorize every future portfolio/trade/report operation against the signed-in
  domain account. Never trust a client-supplied owner ID. Add tests proving that
  another account cannot read or mutate the resource.
- Use `decimal` for quantities, fees, amounts, and rates. Do not round intermediate
  calculations or overwrite stored source values with display-rounded values.
- FIFO is mandatory. Order by broker execution time and the documented stable-ID
  tie-breaker; UUID generation time does not replace trade execution time.
- Convert purchase amounts/fees at the purchase-date rate and sale amounts/fees at
  the sale-date rate. UAH profit is not USD profit multiplied by one rate.
- Splits preserve total acquisition cost, allocated purchase fees, and lot order.
  Preserve immutable trade inputs and reproducible, versioned calculation results.

## Design references

- [FIFO decision](docs/decisions/0001-fifo-tax-lot-matching.md)
- [Spreadsheet-derived calculation specification](docs/domain/tax-calculation-specification.md)
- [Ledger model and database invariants](docs/domain/investment-ledger-model.md)
- [Authentication and first-admin setup](docs/security/authentication.md)
- [Local setup](README.md) and [CI commands](.github/workflows/ci.yml)

The ledger document describes the initial persistence stage and still lists
authentication as deferred. Authentication is now implemented; use its dedicated
guide and current code for that status. Unresolved tax/rate rules in the
calculation specification remain unresolved.

## Development progress

Baseline inspected on 2026-09-05, at commit `dcc53ad`.

- [x] Backend foundation: .NET 10 solution and layer references, Swagger/OpenAPI,
  health endpoints, PostgreSQL/EF Core, local migration tooling, Dockerfile,
  unit/integration test projects, and build/test CI.
- [x] FIFO calculation core: IBIT/TLT spreadsheet regression cases, fee allocation,
  separate buy/sell FX conversion, split adjustment, and overselling rejection.
  This is a domain calculator, not a finished tax-report feature.
- [x] Ledger persistence foundation: accounts, portfolios, instruments, trades,
  rates, splits, calculation runs, match snapshots, constraints, and persistence tests.
  These models do not yet provide portfolio/trade HTTP workflows.
- [x] Authentication: Identity credentials linked to domain accounts, login/logout,
  current-account and CSRF endpoints, super-admin account creation, first-admin
  bootstrap, cookie settings, password/lockout policy, and integration tests.
- [x] UUIDv7 for generated entity IDs and Visual Studio folder visibility fixes.

## Remaining development steps

The following is the proposed continuation order; complete each with relevant
tests and a documented manual check before moving on.

1. [ ] Portfolio management API: account-scoped list/create/update and an explicit
   archive/delete policy. Verify unauthenticated access, cross-account isolation,
   duplicate-name handling, and populated-portfolio behavior.
2. [ ] Instrument catalog and manual trade entry: stock/ETF selection, purchases,
   sales, fees, validation, pagination, duplicate broker-ID behavior, and an
   audited correction workflow. Verify ownership and invalid/oversold trades.
   Coordinate rate selection with step 3 before treating entries as tax-ready.
3. [ ] NBU exchange-rate integration: dated USD/UAH retrieval, caching/provenance,
   retries, and an explicit weekend/holiday/missing-rate policy. Resolve the
   transaction-date/timezone rules; test using fixed responses and failure cases.
4. [ ] Split management and holdings/realized-results workflows: persist authorized
   split events, load the ledger into the existing FIFO engine, and expose results.
   Test partial/multiple lots, event ordering, and historical recalculation.
5. [ ] Tax reports: persisted versioned runs and matches, year/account scope,
   export format, reconciliation with the spreadsheets, and agreed rounding.
   Validate current Ukrainian filing requirements before calling reports filing-ready.
6. [ ] Performance/statistics and S&P 500 comparison: define cash-flow and
   dividend treatment, price data source/licensing, valuation dates, currency,
   and price-return versus total-return benchmark methodology before implementing.
7. [ ] Later product workflows: broker imports, dividends/withholding, deposits,
   withdrawals, transfers preserving original lots, and account recovery/lifecycle.
   Refine their priority when needed by reporting or performance work.
8. [ ] Production readiness with infra/web: persistent Data Protection keys,
   operational logging, authentication abuse controls, migration/deployment
   procedure, backups/restore, and an end-to-end acceptance test.

Coordinate frontend foundation and login with the web repository; they can proceed
using the existing auth endpoints before all investment APIs are complete.

## Verification and migrations

Run from this repository root. For backend changes, use the CI sequence:

```powershell
dotnet tool restore
dotnet restore ZiApp.sln --locked-mode
dotnet build ZiApp.sln --configuration Release --no-restore
dotnet test ZiApp.sln --configuration Release --no-build
git diff --check
```

Integration tests need Docker and create disposable PostgreSQL containers with
real migrations. If Docker is unavailable, report that integration tests were
not run; unit tests alone are not a full verification result. For documentation-only
changes, check accuracy, paths, and whitespace; a full backend test run is unnecessary.

Migrations live in `src/ZiApp.Infrastructure/Persistence/Migrations`.
The current chain is `InitialFoundation`, `InitialInvestmentLedger`, then
`AddIdentityAuthentication`. The model snapshot is not another migration.

```powershell
dotnet ef migrations add MigrationName --project src/ZiApp.Infrastructure --startup-project src/ZiApp.Api --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project src/ZiApp.Infrastructure --startup-project src/ZiApp.Api
dotnet ef migrations list --project src/ZiApp.Infrastructure --startup-project src/ZiApp.Api
dotnet ef database update --project src/ZiApp.Infrastructure --startup-project src/ZiApp.Api
```

Add a migration only for model/schema changes and inspect the generated operations.
Prefer forward migrations over rewriting applied history. For custom local
connections, the design-time factory reads `ConnectionStrings__Database`.
Inspect the target database before applying migrations or a destructive reset.
API startup does not automatically apply migrations; use the history table or
migration listing to distinguish existing files from applied migrations.
