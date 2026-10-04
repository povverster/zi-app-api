# Year-specific, user-configured tax reports

Implemented 2026-10-04. This is the current product direction, following the
user's clarification: use the spreadsheet/FIFO calculation and user-entered
yearly rates to generate useful tax reports. A specialist review is **not a
prerequisite** for this configurable calculation tool. Official declaration
forms, submission and statutory-policy certification are separate future work.

## Confirmed decisions and limits

- One portfolio per report, for a selected year from 2000 through the current
  server UTC year. Current-year reports contain entered activity so far.
- Keep the spreadsheet-derived FIFO, fees, separate buy/sell UAH conversion and
  unchanged broker calendar dates. Earlier lots still participate in FIFO.
- Settings belong to the signed-in account and year, shared by that account's
  portfolios. Save explicitly; never copy today's rates into earlier years.
- The user's example is 18% investment income tax, 5% military tax, and 9%
  dividend income tax. These are **user inputs, not hardcoded legal defaults**.
- The user explicitly chose sales reports now and dividend calculations later.
  Store the dividend percentage, but mark dividends `NotIncluded`. Do not
  calculate a dividend total of zero or apply 9% to stock/ETF sale profits.
- The user clarified that a loss means zero taxes **and a visible negative loss
  amount**, for example loss `-10000` and total tax `0.00`.
- This stage does not apply prior-year loss deductions, external investment
  amounts, withholding credits, exemptions, or cross-portfolio netting.
  The existing [annual preparation](annual-preparation-drafts.md) workflow remains
  separate, 2025-only and unchanged; its claims are still unverified/not deducted.

## Calculation and precision

The source is an explicitly selected immutable [portfolio draft](draft-tax-reports.md).
Its `totals.profitUah` is the selected year's signed net realized UAH result,
including gains, losses, acquisition costs and allocated fees.

```text
netProfitUah     = source.totals.profitUah
lossUah          = min(netProfitUah, 0)       // negative or zero, never positive
taxBaseUah       = max(netProfitUah, 0)
investmentTaxUah = round2(taxBaseUah * investmentIncomePercent / 100)
militaryTaxUah   = round2(taxBaseUah * militaryPercent / 100)
totalTaxUah      = investmentTaxUah + militaryTaxUah
```

Each final tax rounds to two places, half away from zero; intermediate source
values are unchanged. The total sums the two individually rounded amounts.
This is a versioned product rounding convention, not a claim about official
form rounding. All financial API values are invariant decimal strings.

| Net result | Loss field | Tax base | Income tax at 18% | Military at 5% | Total |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 10000 | 0 | 10000 | 1800.00 | 500.00 | 2300.00 |
| -10000 | -10000 | 0 | 0.00 | 0.00 | 0.00 |
| 0 | 0 | 0 | 0.00 | 0.00 | 0.00 |

The loss is the report's current-year arithmetic loss, available for the user's
declaration preparation. It is not automatically an accepted carryforward,
refund, deduction against dividends, or taxpayer-wide filing result.

The calculator uses exact integer coefficients from decimal inputs to round
each final tax once, avoiding intermediate multiplication overflow or half-cent
double rounding. Amounts outside the supported decimal monetary range fail.
Policy versions:

- `positive-annual-net-configured-rates-v1` (the floor affects taxes, not losses);
- `each-tax-2dp-half-away-from-zero-v1`;
- snapshot `ziapp-configured-tax-report-v1`.

## Settings API

Every endpoint requires an active account; POST also requires a fresh CSRF token.
No owner ID is accepted. Super administrators have no access to another user's
settings or reports. Responses are no-store.

| Method | Path | Result |
| --- | --- | --- |
| POST | `/api/tax-settings/{year}` | 201: append an immutable settings revision |
| GET | `/api/tax-settings/{year}` | Latest revision, or 404 when not configured |
| GET | `/api/tax-settings/{year}/history` | Paginated revisions, newest first |

Example POST to `/api/tax-settings/2025`:

```json
{
  "investmentIncomePercent": "18",
  "militaryPercent": "5",
  "dividendIncomePercent": "9"
}
```

All three percentages are required decimal strings, between 0 and 100 inclusive,
with at most four fractional places. Zero is valid; exponent notation, signs,
commas, whitespace and JSON numbers are rejected. Returned percentages are
canonical strings. Years have the same bounds as portfolio drafts.

POST always creates a new UUIDv7 revision with `id`, `taxYear`, all percentages
and `createdAtUtc`. There is no update/delete endpoint. Latest means greatest
creation time, then ID. Simultaneous saves both remain in history. Report creation
selects an explicit revision ID, so a concurrent settings change cannot silently
change the chosen rates. Repeated identical saves are not deduplicated.

## Configured report API

Base: `/api/portfolios/{portfolioId}/configured-tax-reports`.

| Method | Suffix | Result |
| --- | --- | --- |
| POST | none | 201: save a report; CSRF required; Location points to GET |
| GET | none | Private paginated history, optional `taxYear` |
| GET | `/{id}` | Saved document |
| GET | `/{id}/export?format=json` | Exact complete saved JSON attachment |
| GET | `/{id}/export?format=csv` | One-row tax/loss summary attachment; default format |
| GET | `/{id}/current-status` | Separate current-settings/source comparison |

POST body (replace placeholders with returned IDs):

```json
{
  "sourceReportId": "<saved-portfolio-report-id>",
  "settingsId": "<saved-settings-revision-id>"
}
```

The year comes from the selected portfolio draft; the settings must match it.
Both resources must belong to the caller, and the draft must belong to the URL
portfolio. Archived owned portfolios are supported. Missing rates must be resolved
before creating the underlying portfolio draft, not by the configured-report API.
There are no NBU requests, ledger mutations, or automatic "latest report" selections.

Creation uses Repeatable Read, validates the source snapshot's integrity and
supported policy versions, calculates taxes and saves one immutable snapshot.
The document captures the full source report, its canonical serialized digest,
all selected settings, the signed loss, tax amounts, actor/time and policy versions.
It reports `status: "UserConfigured"`, `incomeScope: "PortfolioStockEtfSalesOnly"`,
`dividendStatus: "NotIncluded"` and `isTaxReady: false`. The latter means no official
filing certification; it does **not** mean configurable tax arithmetic is absent.

Each POST saves a new report. Changing settings, correcting trades or renaming a
portfolio does not rewrite old snapshots/exports. To use corrected ledger inputs,
create a new portfolio draft, then a new configured report. To change only rates,
select the existing source draft and a different same-year revision.

Current-status returns `latestSettingsId`, `usesLatestSettings`, `sourceStatus` and
`matchesCurrentSourceInputs`; it never rewrites a report or asserts filing readiness.
Settings and ledger comparisons are separate observations, not a synchronized
global snapshot. Explicit historical settings remain valid even when not latest.
Read/export verifies the saved hash and embedded calculation before returning it.
Hashes detect inconsistency; they are not digital signatures.

History endpoints use `page=1` and `pageSize=20` (maximum 100), ordered by creation
time then ID descending. A list and count can observe concurrent new records.
Source snapshots are capped at 16 MiB; configured snapshots at 20 MiB. Fail rather
than truncate. Missing/foreign resources return 404, inactive/anonymous users 401.
Business errors return `code`:

- 400: `InvalidInput`, `YearMismatch`.
- 409: `InvalidSource`, `LegacySnapshotUnavailable`, `InvalidSnapshot`, `TooLarge`,
  `ArithmeticNotRepresentable`.
- Framework binding/CSRF errors may be 400 without a business code.

CSV uses fixed English headings, quoted cells, UTF-8 BOM and CRLF records.
The user-controlled portfolio name is prefixed with a literal apostrophe against
formula injection. Financial values remain strings; import them as text to preserve
precision. It includes `NetProfitUah`, negative `LossUah`, `TaxBaseUah`, separate
taxes, total, rates and `DividendIncomePercentNotApplied`. For full FIFO detail,
use the embedded JSON source or the original draft CSV. Neither export is an
official declaration template.

## Migration and verification

Apply `20261004144649_AddConfiguredTaxReports` after
`20261003152645_AddAnnualPreparationDrafts` using the
[migration procedure](../../README.md#apply-pending-migrations).
It adds `tax_settings_revisions` and `configured_tax_reports` with restrictive FKs.
It does not rewrite existing trades, rates, reports or annual snapshots.
Downgrade refuses to erase either saved settings or configured reports.

Confirm/back up the target and use the established controlled schema window.
No database reset, new service, credential, scheduler or Compose change is needed.
API startup and tests do not migrate the user's database. Backups must preserve
settings revisions and private report snapshots with their sources.

Acceptance:

1. Create settings for a chosen year, save a portfolio draft for that year, then
   create/read/export a configured report with the two returned IDs.
2. Confirm `10000` profit gives `1800.00` and `500.00` at 18/5, and `-10000` gives
   a visible `lossUah: "-10000"` with both taxes `"0.00"`.
3. Save different-year rates and verify no cross-year reuse. Save a new same-year
   revision; old JSON must remain byte-identical while current-status changes.
4. Check a second account/non-owner admin, missing CSRF, archived portfolios,
   invalid percentages, corrupt/legacy snapshots and unsupported formats.
5. Compare BND/BNDX/corrected-BXMT signed losses through both report workflows.

Automated verification results are recorded in the API [agent guide](../../AGENTS.md).
On 2026-10-04, tooling and locked restore succeeded; Release build had zero
warnings/errors; all 348 tests passed (126 unit, 222 PostgreSQL integration,
none skipped). EF reported no pending model changes. All 35 changed files use
LF; 104 local documentation link targets and all three Git diff checks passed.
The test suite includes mixed gains/losses within one year and an empty year.
No user database was migrated and no commit/push was made.
The source workbooks remain read-only and absent from Git/CI. BND/BNDX/TLT were
re-inspected read-only; their fingerprints match the [audit](../domain/spreadsheet-sample-audit.md).
BXMT/SCHD fingerprints also match. Unit rate tests use audited TLT/SCHD subtotals;
they do not claim a new complete SCHD split/broker-history reconstruction.

## Handoff to the next AI

1. Read all three AGENTS guides, this contract and Git status; preserve work.
2. This product clarification supersedes the earlier blocking specialist-review
   step and review-package request. Do not reopen that blocker for user-configured
   reports. Keep the old research as optional official-filing background.
3. As of 2026-10-05, the sibling web foundation is implemented (React/TypeScript/
   Vite, locales, routing, API proxy, tests and CI). The next shared stage is
   authentication UI, not another foundation or tax-policy research loop.
   Follow the [web handoff](../../../zi-app-web/docs/frontend-foundation.md);
   report/settings screens follow in its ordered bounded stages. The configured
   reporting stage itself added no UI and this contract's calculations are unchanged.
4. Dividends are still deferred by explicit user choice. The saved 9% example
   rate is not an implemented dividend ledger or withholding calculation.
5. Preserve one-portfolio drafts, the separate 2025 annual preparation API,
   original broker dates, FIFO, and historical report/settings snapshots.
6. Do not silently apply carryforward claims or sum individual portfolio tax
   amounts into a taxpayer-wide bill. Request scope before adding those workflows.
7. Verify actual migration state on the destination PC; passing disposable-DB
   tests says nothing about the user's database. Commit/push only when requested.
