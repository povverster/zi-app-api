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
  Quantity-changing writers must take the shared ledger advisory lock BEFORE a
  portfolio row lock; global split writers take its exclusive side before locking
  affected portfolios in ID order. Future import/transfer/void writers must comply.
- FIFO is mandatory. Order by broker execution time and the documented stable-ID
  tie-breaker; UUID generation time does not replace trade execution time.
- Convert purchase amounts/fees at the purchase-date rate and sale amounts/fees at
  the sale-date rate. UAH profit is not USD profit multiplied by one rate.
- Splits preserve total acquisition cost, allocated purchase fees, and lot order.
  Preserve immutable trade inputs and reproducible, versioned calculation results.
- Shared instrument splits are readable by active users and writable only by
  super admins with CSRF. Corrections append revisions, retaining their FIFO key.
  Validate all affected owners/archived portfolios without exposing private IDs.
- Holdings use `fifo-uah-v2-remaining-cost`: track remaining lot cost/fees; split
  quantity is quantity * numerator / denominator, not quantity * rounded factor.
  Keep the original v1 match entry point intact for historical calculations.
  Pending/unverified rates suppress an instrument's financials and portfolio totals.
  A historical cutoff uses current revisions, not what was recorded at that date.
  Holdings GET uses a consistent snapshot and never fetches rates or saves reports.
- Saved reports cover one portfolio and one broker calendar year, including archived
  owned portfolios. Replay preceding FIFO history, but total only that year's sales.
  Keep legacy broker dates unchanged. All included replay trades require verified FX.
- Reports are immutable drafts, always `isTaxReady: false`. Preserve the exact input
  trade/split/rate revisions, provenance, policy versions and full decimal results.
  GET/export reads the saved snapshot; current-status compares inputs without rewriting it.
  Creation uses Repeatable Read and atomically saves the snapshot and match rows.
- Calculated report match values use unconstrained PostgreSQL `numeric`; source
  precision is unchanged. Never add implicit 12-place rounding to calculated results.
  CSV protects user text from formula injection; preserve financial strings end to end.
  Official forms, tax rates/payable and final filing rounding are not implemented.
- The first filing research scope is 2025, Ukrainian tax-resident individuals,
  personal foreign-broker stock/ETF sales only. Research findings are not legal
  approval. Preserve draft policies and broker dates while legal FX/fee/loss/
  rounding/form gates remain open; never enable readiness from sample agreement.
- The user approved a separate taxpayer-year summary including relevant portfolios,
  external investments and prior-loss claims. Keep one-portfolio drafts unchanged.
  Annual preparation drafts are implemented separately for 2025: explicit owned
  report IDs, compatible policy tuples, signed exact subtotals, external coverage
  and unverified loss claims. Claims do not reduce subtotals. Require explicit
  coverage/overlap review; unknown amounts stay null and readiness stays false.
  Preserve immutable snapshots and keep current-source status separate. Follow
  the annual contract; legal validation remains a later explicitly confirmed stage.
- Personal spreadsheets in sibling `zi-samples` are read-only references, not CI
  dependencies or legal authority. Do not copy them into Git. The sample audit
  records SCHD split/year cases and the user-confirmed BXMT price correction.

## Design references

- [FIFO decision](docs/decisions/0001-fifo-tax-lot-matching.md)
- [Spreadsheet-derived calculation specification](docs/domain/tax-calculation-specification.md)
- [Ledger model and database invariants](docs/domain/investment-ledger-model.md)
- [Authentication and first-admin setup](docs/security/authentication.md)
- [Portfolio API contract and acceptance checks](docs/portfolios/portfolio-management.md)
- [Instrument/trade contract, corrections, and migration](docs/trading/manual-trade-entry.md)
- [NBU rates, broker-date policy, provenance, and migration](docs/exchange-rates/nbu-exchange-rates.md)
- [Splits, holdings/FIFO results, concurrency, and migration](docs/holdings/splits-and-holdings.md)
- [Saved draft reports, year scope, provenance, exports, and migration](docs/reports/draft-tax-reports.md)
- [Annual preparation drafts, coverage, exact sums and migration](docs/reports/annual-preparation-drafts.md)
- [2025 filing research and open review gates](docs/reports/ua-2025-filing-readiness.md)
- [Additional spreadsheet sample audit and correction provenance](docs/domain/spreadsheet-sample-audit.md)
- [Local setup](README.md) and [CI commands](.github/workflows/ci.yml)

Authentication, portfolios, manual trades, NBU resolution, splits, holdings, and saved
draft reports and separate annual preparation drafts are implemented.
Use their dedicated guides and current code for HTTP contracts. Filing research
is documented, but legal validation and official tax/form calculations remain pending.

## Development progress

Status updated on 2026-10-04 for annual preparation drafts, starting from
`2534ca8`. Prior saved-report checks remain in the [report guide](docs/reports/draft-tax-reports.md).

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

- [x] Split management and holdings: admin-only global split creation/correction
  with provenance and stable ordering; owner-only positions, open lots and FIFO
  results with historical cutoffs and explicit rate blockers. Archived portfolios
  participate. Versioned remaining-cost calculation preserves original v1 behavior.
  Requires `AddSplitManagement`; no reset or user-database migration was performed.
  PostgreSQL tests cover migration/audit preservation, account isolation, CSRF,
  concurrent sells/splits and the first-trade locking boundary. See the holdings
  guide for scope and verification; saved reports follow below and UI remains pending.
  Tooling/locked restore and Release build passed (zero warnings/errors);
  all 206 tests passed (49 unit, 157 integration, none skipped). EF reports no
  pending model changes. Changed-file LF, documentation links and diff checks passed.

- [x] Saved draft report API: one portfolio/year, full-history FIFO with broker-year
  selection, immutable source/provenance snapshots, versioned matches and full-precision
  USD/UAH totals, paginated history, current-input comparison, and CSV/JSON downloads.
  Includes owner/CSRF checks, rate blockers, archived portfolios, integrity checks,
  legacy-run handling, spreadsheet reconciliation and concurrent-correction tests.
  Requires `AddDraftTaxReports`; computed matches retain .NET decimal precision.
  Official filing rules, tax payable and final rounding are deliberately deferred.
  Tooling/locked restore and Release build passed with zero warnings/errors;
  all 241 tests passed (59 unit, 182 integration, none skipped). EF reports no
  pending model changes. See the report guide for scope and acceptance checks.
  The user's DB was not migrated; frontend implementation remains pending.

- [x] Filing research baseline: user-confirmed 2025 scope, separate annual-summary
  decision, official-source findings, unresolved legal/form/rounding gates and a
  bounded implementation brief. Inspected eight added spreadsheets read-only;
  recorded SCHD split/prior-year cases and the BXMT correction to 18.08 USD.
  This is documentation/analysis only, not completed legal validation. No runtime,
  schema, user database or frontend change. Documentation checks are recorded in
  the research guide; the previous 241 tests were not rerun for this stage.

- [x] Owner-only 2025 annual drafts with explicit report selection, hashes/policies,
  signed exact subtotals, external-coverage inputs, unverified prior-loss claims,
  overlap checks, immutable JSON export and separate current-source status.
  Requires `20261003152645_AddAnnualPreparationDrafts`; downgrade protects saved
  annual snapshots. Existing portfolio reports and user database were not changed.
  BND/BNDX/corrected-BXMT regressions run through both report APIs; source workbooks
  remain read-only and are not CI dependencies. See the annual guide for limits.
  Tooling/locked restore and Release build passed with zero warnings/errors;
  all 294 tests passed (93 unit, 201 integration, none skipped), using disposable DBs.
  EF reports no pending model changes. Changed-file LF, local documentation links
  and whitespace checks passed across all three repositories.
  Frontend code, official taxes/forms, accepted loss deductions and filing readiness
  remain unimplemented.

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
4. [x] Split management and holdings/realized-results workflows: audited global
   splits and corrections, private positions/open lots/FIFO matches, resolved-rate
   checks, current-revision historical cutoffs, and stable ordering. V2 preserves
   remaining acquisition cost/fees through partial sales and splits. Partial/multiple
   lots, concurrency, audit retention and migration preservation are tested.
5. [x] Saved draft reports: user-approved one-portfolio/year scope, immutable
   versioned inputs/results, prior-year FIFO consumption, CSV/JSON exports,
   spreadsheet reconciliation, full decimal storage and current-input comparison.
   No filing rounding or taxes payable; all reports remain explicitly drafts.
6. [ ] Filing-readiness work, split into bounded stages:
   - [x] Research baseline and scope decisions for 2025. See the
     [review and next-stage brief](docs/reports/ua-2025-filing-readiness.md).
     This does not mean legal validation is complete.
   - [x] Annual preparation draft API: explicit owned snapshots, external coverage,
     prior-loss claims, known-overlap protection, exact signed subtotals, immutable
     JSON exports and separate current-source status. Owner/admin isolation, CSRF,
     integrity, precision, source corrections and migration preservation are tested.
     See the [annual contract](docs/reports/annual-preparation-drafts.md).
   - [ ] **Next stage: close the documented 2025 legal/FX/fee/FIFO/loss/rounding/form review gates**
     and obtain explicit user confirmation before implementing official calculations.
     Then implement separately versioned policy/form support with reviewed fixtures.
     Do not change readiness or historical drafts from spreadsheet agreement alone.
7. [ ] Performance/statistics and S&P 500 comparison: define cash-flow and
   dividend treatment, price data source/licensing, valuation dates, currency,
   and price-return versus total-return benchmark methodology before implementing.
8. [ ] Later product workflows: broker imports, dividends/withholding, deposits,
   withdrawals, transfers preserving original lots, audited trade cancellation,
   and account recovery/lifecycle.
   Refine their priority when needed by reporting or performance work.
9. [ ] Production readiness with infra/web: persistent Data Protection keys,
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
`AddIdentityAuthentication`, `AddManualTradeEntry`, `AddNbuExchangeRates`, then
`20261002125349_AddSplitManagement`, then `20261002140835_AddDraftTaxReports`,
then `20261003152645_AddAnnualPreparationDrafts`.
The model snapshot is not another migration. These upgrades preserve existing data.
Manual-trade downgrade blocks loss of pending rates/correction history; NBU downgrade
blocks loss of response provenance/resolution history; split downgrade blocks loss
of split provenance/correction history. Draft-report downgrade refuses to erase
saved snapshots or coerce calculated matches back to numeric(28,12) with data loss.
Annual-preparation downgrade refuses to erase any saved annual snapshot.
Use forward migrations and a controlled schema window, backing up the confirmed target.
Drain older API
writers before running the split stage: all quantity writers must use its new lock.

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
