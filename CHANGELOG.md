# Changelog

Notable changes to the ZiApp backend are recorded here, using the
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.

The initial entries summarize completed development work from Git history.
They remain unreleased until assigned to an actual versioned release.
Planned work and development instructions are in [AGENTS.md](AGENTS.md).

## [Unreleased]

### Added

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

- Include timezone data in the Alpine API image for the NBU current-date guard.
  Broker transaction dates themselves are never timezone-converted.
- Copy the existing SDK policy and analyzer configuration into Docker builds so
  container publishing uses the same migration-specific rules as local builds.

- Visual Studio visibility of domain source files and Application/Infrastructure
  feature folders through explicit project folder and compile-item visibility settings.
