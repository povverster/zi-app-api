# Changelog

Notable changes to the ZiApp backend are recorded here, using the
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format.

The initial entries summarize completed development work from Git history.
They remain unreleased until assigned to an actual versioned release.
Planned work and development instructions are in [AGENTS.md](AGENTS.md).

## [Unreleased]

### Added

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

- Fixed local API addresses at HTTP port `5050` and HTTPS port `5051`.
- New account and Identity user IDs, plus generated test IDs, use UUIDv7.
  Existing IDs and deterministic seed IDs are preserved; the database type
  remains `uuid`, so this generation change requires no schema migration.
- Standardized text files on LF line endings through Git and editor settings.

### Fixed

- Visual Studio visibility of domain source files and Application/Infrastructure
  feature folders through explicit project folder and compile-item visibility settings.
