# Changelog

Notable changes to the ZiApp backend are recorded here, using the
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.

The initial entries summarize completed development work from Git history.
They remain unreleased until assigned to an actual versioned release.
Planned work and development instructions are in [AGENTS.md](AGENTS.md).

## [Unreleased]

### Added

- Separate owner-only 2025 annual preparation drafts: explicitly selected portfolio
  reports, preserved hashes/policies, exact signed subtotals, external-coverage
  inputs and unverified prior-loss claims. Unknown is distinct from zero; loss
  claims do not reduce results and every draft remains non-filing-ready.
- Immutable annual snapshots, paginated history, exact JSON downloads, separate
  current-source comparisons, compatible-policy and known-overlap checks, bounded
  inputs and arithmetic that rejects overflow or precision loss.
- `AddAnnualPreparationDrafts` migration after `AddDraftTaxReports`. It preserves
  existing data and refuses a downgrade that would erase annual drafts.
- Annual API/account-isolation/CSRF/integrity/correction/migration tests and BND,
  BNDX and corrected-BXMT sample regressions. Source workbooks remain untouched;
  there is no new frontend, tax-payable calculation or official form.

- A 2025 Ukrainian filing-readiness research baseline with official sources,
  user-confirmed scope, unresolved validation gates and the approved separate
  annual-summary design. Research does not enable tax payable or filing readiness.
- Read-only audit of eight additional spreadsheet samples, including SCHD's split
  and year selection and the user-confirmed BXMT purchase-price correction.
  No runtime, database migration or source-workbook change in this documentation stage.

- Owner-only saved draft tax reports for one portfolio and broker calendar year,
  replaying prior FIFO history while totaling only that year's sales. Archived
  portfolios are supported; missing verified rates block creation without partial rows.
- Immutable, versioned JSON input/result snapshots, exact split/rate provenance,
  integrity digests, paginated report history, current-input comparison and CSV/JSON
  downloads. Creation uses a consistent database snapshot; corrections never rewrite
  older reports. CSV protects user-controlled text against formula injection.
- `AddDraftTaxReports` migration after `AddSplitManagement`, preserving existing runs
  and source data. Apply before using reports; no reset is needed. Downgrade protects
  saved snapshots and calculated values that cannot safely return to numeric(28,12).
- Report regression tests for annual spreadsheet reconciliation, broker-year/legacy
  dates, prior-year lot consumption, precision, isolation/CSRF, snapshot retention,
  CSV safety, migration guards and simultaneous ledger corrections.

- Super-admin split creation and audited replacement corrections with retained
  source/actor/time, immutable originals, stable FIFO ordering and paginated history.
  Shared corporate actions validate affected holdings across all owners, including
  archived portfolios, without granting access to private portfolios.
- Owner-only holdings with inclusive historical cutoffs, quantities, open FIFO lots,
  realized matches, exact decimal-string USD/UAH results and source trade/rate IDs.
  Pending/unverified rates suppress incomplete financial results; GET never fetches
  rates or persists reports. Historical projections use current corrected inputs.
- `AddSplitManagement` forward migration after `AddNbuExchangeRates`, preserving
  existing split IDs/ratios and protecting new provenance/correction history against
  downgrade. Apply before using the workflow; no database reset is required.
- Regression coverage for split precision, rate readiness, historical recalculation,
  isolation/CSRF, simultaneous trade/split writes, audit history and migration safety.

- Exact-date NBU USD/UAH retrieval and immutable database cache, with original JSON,
  response digest, source URL, calculation date, retrieval time, strict payload
  validation, bounded timeouts/retries, and missing-rate handling without fallback.
- Owner-scoped, CSRF-protected trade-rate resolution and provenance endpoints,
  preserving broker calendar dates without timezone conversion, including old
  records. One-time assignment records policy, selected date, actor, and time;
  legacy links and corrected originals keep their existing history.
- `AddNbuExchangeRates` migration after `AddManualTradeEntry`, plus date, API,
  cache-concurrency, audit, and data-preserving migration regression tests.
  Apply before using the rate workflow; no reset is needed. Downgrade is blocked
  when new provenance/resolution history exists.

- Shared, searchable stock/ETF catalog with super-admin creation and owner-scoped
  manual buy/sell endpoints, fees, pagination, exact decimal-string contracts,
  preserved broker timestamp offsets, and explicit pending exchange-rate status.
- Audited trade corrections that retain original inputs and report snapshots;
  stable FIFO ordering across replacements, broker-ID conflict handling, archived
  portfolio protection, and transaction-safe concurrent overselling checks.
- `AddManualTradeEntry` forward migration and regression tests for existing-data
  upgrades, audit preservation, access control, precision, splits, and concurrency.
  Apply the migration before using the new API; no database reset is required.
- Authenticated, owner-scoped portfolio creation, paginated listing, retrieval,
  renaming, and reversible archive/restore, with USD defaults, UUIDv7 IDs, CSRF
  protection, active-account checks, and duplicate-name conflict responses.
  Archived portfolios retain their trades and report snapshots; hard deletion
  is unavailable. The existing schema supports these operations without a new migration.
- Portfolio API documentation and regression coverage for ownership, validation,
  pagination, concurrent duplicate names, archiving, and historical-data preservation.

- ASP.NET Core/.NET 10 backend with separate API, Application, Domain, and
  Infrastructure projects, PostgreSQL persistence through EF Core/Npgsql,
  and development setup documentation.
- Swagger/OpenAPI in Development and Testing, with process liveness and
  database readiness endpoints.
- Repository-local EF Core tooling and the `InitialFoundation`,
  `InitialInvestmentLedger`, and `AddIdentityAuthentication` migrations.
- FIFO realized-gain calculator with partial and multiple-lot matching,
  proportional fee allocation, independent purchase/sale USD-to-UAH conversions,
  split adjustments that preserve acquisition cost, and overselling rejection.
  Regression tests reproduce the IBIT and TLT spreadsheet examples.
- Investment-ledger entities and persistence for accounts, portfolios,
  instruments, trades, exchange-rate records, splits, calculation runs, and
  FIFO match snapshots, including ownership relationships and database constraints.
- ASP.NET Core Identity linked to domain accounts, cookie-based login/logout,
  current-account lookup, super-admin account provisioning, and first-admin
  bootstrap. Public registration is unavailable.
- CSRF protection for login, logout, and account creation; HTTP-only cookies,
  HTTPS-required production cookies, password requirements, failed-login lockout,
  and API-friendly unauthorized/forbidden responses.
- Unit tests, PostgreSQL integration tests using disposable containers and real
  migrations, CI builds/tests, package lock files, and a non-root API Docker image.
- Architecture decisions, calculation/persistence/authentication documentation,
  and an `AGENTS.md` guide with development milestones and changelog maintenance rules.

### Changed

- Calculated report match columns now use unconstrained PostgreSQL `numeric` to
  retain .NET decimal results without extra database rounding; source input precision
  is unchanged. Reports remain drafts, without tax payable, official forms or filing
  rounding. Legacy runs without full snapshots are preserved, not reconstructed.

- Holdings use the versioned `fifo-uah-v2-remaining-cost` calculator: keep remaining
  cost/fees through splits and allocate the final remainder on lot exhaustion.
  The original v1 match calculator and stored historical reports are preserved.
- Quantity writers coordinate using a shared/exclusive ledger advisory lock before
  portfolio row locks, including first trades during global split changes. Drain
  old API writers before upgrading. Holdings use a consistent Repeatable Read snapshot.

- Trade FX links may be null until explicit NBU resolution; statuses distinguish
  Pending, LinkedUnverified, and Resolved, all still not tax-ready.
  Broker IDs are unique per portfolio
  among current trades, while superseded source records remain available for audit.
- Fixed local API addresses at HTTP port `5050` and HTTPS port `5051`.
- New account and Identity user IDs, plus generated test IDs, use UUIDv7.
  Existing IDs and deterministic seed IDs are preserved; the database type
  remains `uuid`, so this generation change requires no schema migration.
- Standardized text files on LF line endings through Git and editor settings.

### Fixed

- Reverse-split quantity adjustment multiplies before division, avoiding a rounded
  split factor that could make three units in a 1-for-3 split insufficient to sell
  one unit. Positive quantities below decimal precision and overflow fail safely.

- Include timezone data in the Alpine API image for the NBU current-date guard.
  Broker transaction dates themselves are never timezone-converted.
- Copy the existing SDK policy and analyzer configuration into Docker builds so
  container publishing uses the same migration-specific rules as local builds.

- Visual Studio visibility of domain source files and Application/Infrastructure
  feature folders through explicit project folder and compile-item visibility settings.
