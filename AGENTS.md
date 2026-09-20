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

## Changelog maintenance

- Update [CHANGELOG.md](CHANGELOG.md) under `Unreleased` in the same task as each
  notable completed feature, behavior change, fix, or security improvement.
- Use the relevant Keep a Changelog categories: `Added`, `Changed`, `Deprecated`,
  `Removed`, `Fixed`, and `Security`. Omit empty categories and describe the
  effect for users or developers rather than copying commit messages.
- Document breaking changes and any required configuration or migration steps.
  Minor formatting edits do not need separate entries.
- Keep future work in this guide's development checklist, not in the changelog.
  For changes spanning repositories, update each affected repository's changelog.
- Move unreleased entries into a version/date section when an actual release is
  made. Do not invent historical releases or treat a commit as a release.
  A changelog update alone does not authorize tagging, publishing, or deployment.

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
- Portfolio names are trimmed and case-sensitive, unique per owner including
  archived portfolios. Creation uses USD. Prefer reversible archive/restore;
  there is no hard-delete endpoint. Trade creation/correction rejects archived portfolios.
- Manual trade amounts are invariant decimal JSON strings, limited to numeric(28,12)
  without rounding. Retain the submitted timestamp/offset as well as normalized UTC.
  New trades have pending nullable FX links; never invent a rate or claim tax readiness.
- The user confirmed all broker dates are correct, including old records: select
  the NBU rate by the original broker calendar date without timezone conversion.
  If the original string is absent, use the already stored date unchanged. Never
  reinterpret legacy dates as Kyiv time. UTC remains for chronological ordering.
- NBU rates use exact-date retrieval, immutable source-response provenance, and
  decimal strings. No weekend fallback, cache overwrite, or legacy-link replacement.
  Resolve pending links explicitly under the portfolio lock; Resolved is not tax-ready.
- Corrections append a replacement and audit record, preserving original inputs and
  saved report snapshots. Use only current (not superseded) trades for current results.
  Preserve `FifoOrderId` across corrections and pass it to the FIFO calculator.
  All future ledger writers must coordinate with the portfolio row-lock transaction.
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
- [Portfolio API contract and acceptance checks](docs/portfolios/portfolio-management.md)
- [Instrument/trade contract, corrections, and migration](docs/trading/manual-trade-entry.md)
- [NBU rates, broker-date policy, provenance, and migration](docs/exchange-rates/nbu-exchange-rates.md)
- [Local setup](README.md) and [CI commands](.github/workflows/ci.yml)

Authentication, portfolios, manual trades, and NBU rate resolution are implemented.
Use their dedicated guides and current code for HTTP contracts. Filing rules and
report rounding in the calculation specification still need separate validation.

## Development progress

Status updated on 2026-09-20 after NBU exchange-rate integration.
This stage started from commit `7ac3875`; its contract and verification are in the
[NBU guide](docs/exchange-rates/nbu-exchange-rates.md).

- [x] Backend foundation: .NET 10 solution and layer references, Swagger/OpenAPI,
  health endpoints, PostgreSQL/EF Core, local migration tooling, Dockerfile,
  unit/integration test projects, and build/test CI.
- [x] FIFO calculation core: IBIT/TLT spreadsheet regression cases, fee allocation,
  separate buy/sell FX conversion, split adjustment, and overselling rejection.
  This is a domain calculator, not a finished tax-report feature.
- [x] Ledger persistence foundation: accounts, portfolios, instruments, trades,
  rates, splits, calculation runs, match snapshots, constraints, and persistence tests.
  Portfolio and manual trade HTTP workflows are now available.
- [x] Authentication: Identity credentials linked to domain accounts, login/logout,
  current-account and CSRF endpoints, super-admin account creation, first-admin
  bootstrap, cookie settings, password/lockout policy, and integration tests.
- [x] UUIDv7 for generated entity IDs and Visual Studio folder visibility fixes.
- [x] Portfolio management: owner-scoped create/list/get/rename/archive/restore,
  CSRF, active-account checks, pagination, duplicate-name handling, and data-preservation
  tests. No new migration was needed. Release build and all 51 tests passed.
- [x] Instrument catalog and manual trades: searchable catalog with admin-only
  additions; owner-scoped buys/sells, exact decimals, pagination, broker-ID conflicts,
  split-aware chronological validation, concurrent-write protection, and audited
  corrections. Original records/rates/report snapshots survive correction.
  `AddManualTradeEntry` is required. Release build and all 100 tests passed;
  EF reports no pending model changes. The user's database was not changed.
- [x] NBU exact-date USD/UAH retrieval/cache, immutable response provenance,
  bounded retries, owner-scoped one-time FX resolution, and documented broker-date
  policy for new and legacy records. Requires `AddNbuExchangeRates`.
  Fixed-response and PostgreSQL tests cover errors, concurrency, and audit retention.
  Locked restore and Release build passed (zero warnings/errors); all 162 tests
  passed (41 unit, 121 integration, none skipped). EF reports no pending model
  changes. Only disposable test databases were migrated, not the user's database.
  The Alpine Dockerfile includes timezone data for the current-date guard and
  copies the existing SDK/analyzer configuration into its build.
  Docker image build passed; a network-disabled check confirmed timezone data
  and non-root execution. Documentation links, LF endings, and diff checks passed.

## Development steps

Continue with the first unchecked step when asked to run the next step. Complete
one stage with relevant tests and a documented acceptance check before moving on.

1. [x] Portfolio management API: owner-scoped list/create/get/rename and reversible
   archive/restore. Hard deletion is unavailable. Account isolation, duplicate names,
   CSRF, and populated-portfolio preservation are tested.
2. [x] Instrument catalog and manual trade entry: stock/ETF selection, purchases,
   sales, fees, pagination, duplicate broker IDs, and audited replacement corrections.
   Ownership, archived portfolios, invalid/oversold trades, concurrency, and upgrade
   preservation are tested. Entries start pending FX resolution, not tax-ready.
3. [x] NBU exchange-rate integration: exact-date retrieval including weekends/
   holidays, missing-rate rejection, provenance, retries, and explicit resolution.
   Use broker dates without conversion, even for old records. Preserve source
   inputs, legacy links, and saved reports. No scheduler/batch import is included.
4. [ ] Split management and holdings/realized-results workflows: persist authorized
   split events, load current nonsuperseded trades with resolved rates into the FIFO
   engine (including their `FifoOrderId`), and expose results.
   Test partial/multiple lots, event ordering, and historical recalculation.
5. [ ] Tax reports: persisted versioned runs and matches, year/account scope,
   export format, reconciliation with the spreadsheets, and agreed rounding.
   Validate current Ukrainian filing requirements before calling reports filing-ready.
6. [ ] Performance/statistics and S&P 500 comparison: define cash-flow and
   dividend treatment, price data source/licensing, valuation dates, currency,
   and price-return versus total-return benchmark methodology before implementing.
7. [ ] Later product workflows: broker imports, dividends/withholding, deposits,
   withdrawals, transfers preserving original lots, audited trade cancellation,
   and account recovery/lifecycle.
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
The current chain is `InitialFoundation`, `InitialInvestmentLedger`,
`AddIdentityAuthentication`, `AddManualTradeEntry`, then `AddNbuExchangeRates`.
The model snapshot is not another migration. These upgrades preserve existing data.
Manual-trade downgrade blocks loss of pending rates/correction history; NBU downgrade
blocks loss of response provenance/resolution history. Use forward migrations.

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
