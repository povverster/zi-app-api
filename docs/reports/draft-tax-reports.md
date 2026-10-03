# Saved draft tax reports

Implemented on 2026-10-03. The user chose **one portfolio per report** and
**saved drafts first**, with CSV/JSON exports at full calculated precision.
These are investment realized-gain worksheets, not official Ukrainian filings.
They do not calculate tax payable or apply tax rates, carryforward rules,
dividend/withholding rules, or filing/display rounding.

## Contract and access

All endpoints require an active signed-in portfolio owner, including when the
caller is a super administrator. Missing/foreign portfolios or report IDs return
404; anonymous/inactive accounts return 401. Reports from archived portfolios
can be created, read and exported. Responses are no-store.

| Method | Path | Result |
| --- | --- | --- |
| POST | `/api/portfolios/{portfolioId}/tax-reports` | 201, saved draft and Location; CSRF required |
| GET | `/api/portfolios/{portfolioId}/tax-reports` | 200, paginated run history |
| GET | `/api/portfolios/{portfolioId}/tax-reports/{id}` | 200, the saved snapshot, not a recalculation |
| GET | `/api/portfolios/{portfolioId}/tax-reports/{id}/current-status` | 200, comparison with current source inputs |
| GET | `/api/portfolios/{portfolioId}/tax-reports/{id}/export?format=csv` | CSV attachment (default format) |
| GET | `/api/portfolios/{portfolioId}/tax-reports/{id}/export?format=json` | Exact saved JSON attachment |

Create with a fresh [CSRF token](../security/authentication.md):

```json
{ "taxYear": 2025 }
```

Years are supported from 2000 through the server's current UTC year. Current-year
reports are drafts of data entered so far, not complete-year certifications.
There is no client-selected calculation version, exchange rate, owner or rounding
mode. There is no account-wide aggregation.

Lists accept optional `taxYear`, `page=1`, and `pageSize=20` (maximum 100),
ordered newest creation time then ID. They return `items`, `page`, `pageSize`,
and `totalCount`. Count and page reads can reflect concurrent new runs.
Repeated POSTs intentionally save separate UUIDv7 runs; they do not overwrite
or deduplicate old reports. No edit/delete/replace endpoint exists.

The draft contains its ID, portfolio/year, creation instant, replay cutoff,
`status: "Draft"`, `isTaxReady: false`, policy versions, input digest, complete
input snapshots, selected-year matches and totals. Amounts and quantities are
invariant decimal JSON strings, never floating-point JSON numbers.

## Annual selection and full-history FIFO

Policy: `broker-calendar-year-v1`.

- Select sales by the year of their original broker calendar date, without
  converting to UTC/Kyiv/browser time.
- For older records without an original timestamp, use the stored calendar date
  unchanged, matching the user-approved legacy rate-date policy.
- Retain normalized UTC for ordering, not year selection.
- For each instrument sold in that year, replay every current trade up to and
  including its latest selected sale instant, plus the current split revisions
  effective through that instant. Earlier-year sales must consume their lots
  before a selected-year sale is matched.
- Only selected-year sale matches enter report totals. Purchases and prior sales
  outside that year remain in the input snapshot when needed for replay.
- A trade from a neighboring broker year can still precede a selected sale in
  UTC ordering at offset boundaries; it participates in replay but not the year's
  totals. Stable ordering keys are passed to the existing v2 FIFO calculator.
- Instruments without selected-year sales, and purchases after an instrument's
  final selected sale, do not contribute to this report or its input digest.
- An empty/no-sale year produces a zero draft, not an error or fabricated trades.
  A malformed recorded timestamp is rejected rather than silently assigned a year.

`replayThroughUtc` is the latest included event instant (null for no sales).
It is not a user-selectable historical knowledge date. Creation uses the current
nonsuperseded revisions, and captures them in a single Repeatable Read database
transaction before atomically saving the run and selected matches. A concurrent
correction therefore cannot mix old and new ledger reads within one report.
It may make a newly saved report stale immediately afterwards, which the status
endpoint can detect.

## Rates and calculation

Reports use `fifo-uah-v2-remaining-cost` from the
[calculation specification](../domain/tax-calculation-specification.md).
Splits, per-lot remaining costs/fees, same-instant ordering and independent
purchase/sale FX follow the [holdings contract](../holdings/splits-and-holdings.md).
Source quantities, fees, timestamps, rates and ordering keys are never rewritten.

Every included replay trade must have a verified NBU assignment with matching
date/policy and recorded provenance. This conservative requirement includes
earlier-year sales in the replay, even though their gains are not totaled.
An unresolved or legacy unverified link returns 409 `UnresolvedRates` with
`rateBlockers` (trade ID and Pending/LinkedUnverified status). No partial report
or match rows are saved. Resolve through the explicit
[NBU workflow](../exchange-rates/nbu-exchange-rates.md), then retry.
Report creation/reads/status/exports never contact NBU or assign rates.

Policy: `full-decimal-no-filing-rounding-v1`. There is no explicit reporting
rounding; .NET decimal remains finite-precision arithmetic. Save its results
without an extra database rounding step. Report match numeric columns are now
unconstrained PostgreSQL `numeric`, preserving all representable .NET decimal
values instead of coercing them to 12 fractional places. Source-trade and rate
column precision is unchanged. See PostgreSQL's
[numeric documentation](https://www.postgresql.org/docs/18/datatype-numeric.html).

## Saved provenance and current-status comparison

Schema: `ziapp-draft-report-v1`.

Each run stores a self-contained JSON snapshot with:

- portfolio ID/name/currency and relevant instrument metadata;
- input trade IDs, original/UTC timestamps, broker dates, sides, exact decimal
  inputs, broker IDs, FIFO keys, selected rate IDs and resolution audit;
- exact current split revisions including original timestamps, ratio, source,
  recording actor/time, correction links and stable ordering keys;
- rate values, effective/calculation dates, source URL, retrieval time, response
  digest and original NBU JSON response;
- calculation/year/precision policy versions, input digest, replay cutoff, FIFO
  matches with source trade/rate IDs, and USD/UAH totals.

The input digest is SHA-256 of the versioned deterministic input serialization.
A separate stored SHA-256 checks the exact snapshot JSON bytes before reads and
exports. These are integrity checks, not digital signatures or protection against
an administrator who can rewrite both data and digests.

GET/export always use the saved snapshot. They do not join current trades to
reconstruct the report. Corrections and new calculations retain older source
inputs and saved results. The relational match rows retain source trade FKs.
The JSON snapshot also preserves inputs that do not have a selected-year match.

Current-status performs a read-only consistent source comparison:

- `Current`: relevant input digest still matches.
- `InputsChanged`: relevant source records or captured metadata changed.
- `CurrentInputsInvalid`: comparison could not safely prepare current inputs
  (for example, invalid data or size limits); matching status is unknown, not true.

The response separately identifies whether the run uses the current calculator
version. A renamed portfolio can change the metadata digest even if financial
values would be identical. Archive state is not part of that digest. New unrelated
instruments or later trades after the last selected sale do not make it stale.
These comparisons do not update the saved run. Generate another draft explicitly
to capture revisions.

Pre-existing calculation runs without full snapshots remain untouched. Lists mark
them `LegacySnapshotUnavailable`; details, status and exports return 409 with
that code. The API never invents missing historical inputs by joining current data.
Generate a new draft while retaining the original run.

## Exports and spreadsheet handling

JSON downloads contain the exact saved document, including all input provenance
and decimal strings. CSV is a flat match worksheet with one Match row per selected
FIFO match and a final Total row. It includes report metadata, source IDs, broker
dates, costs, fees and USD/UAH totals. Both are explicitly Draft/non-filing-ready.
The CSV is not a broker import or official tax-declaration format.

CSV uses fixed English column keys, comma separators, quoted/escaped cells,
CRLF records and UTF-8 with BOM. Portfolio names, symbols and exchange labels are
prefixed with a literal apostrophe, including ordinary text, so formula-like input
cannot become a spreadsheet formula. Quoting alone is insufficient; see
[OWASP's CSV injection guidance](https://community.owasp.org/attacks/CSV_Injection).
JSON retains original text unchanged.

Financial digits in the CSV are not rounded, but spreadsheet programs may infer
numeric cells and truncate their own precision on opening. Import financial
columns **as text** to preserve every digit. Use JSON for an authoritative
machine-readable copy. UI localization and human-formatted reports are deferred.

## Errors and bounded scope

Business errors include a `code` and, when applicable, `rateBlockers`:

- 400: `InvalidInput` (year/pagination/format), `UnsupportedCurrency`.
- 409: `UnresolvedRates`, `InvalidLedger`, `TooLarge`,
  `LegacySnapshotUnavailable`, `InvalidSnapshot`.
- Framework binding/CSRF errors also return 400 but need not include a code.

Ledger reads are capped at 10,000 current trades and 10,000 current splits for
the portfolio's instruments; the saved UTF-8 JSON is capped at 16 MiB. Exceeding
a cap fails rather than truncates. Detail/export is not paginated; large/batch
reporting optimization is deferred. Arithmetic overflow or invalid FIFO history
fails without saving a report. This stage supports USD instruments only.

## Migration, verification and acceptance

Apply **`20261002140835_AddDraftTaxReports`** after `AddSplitManagement` using
the [migration procedure](../../README.md#apply-pending-migrations).
This widens only calculated match numeric columns and adds nullable snapshot
metadata. Existing runs/values/IDs and source data are preserved.
Downgrade refuses to erase saved draft snapshots or coerce values that no longer
fit numeric(28,12). Use a forward migration; no reset is required.

Schema changes need a controlled migration window. Stop/drain old API writers,
back up the intended database and apply the migration before starting updated
instances. Startup does not migrate. Testcontainers use separate disposable DBs,
not the user's development database.

Manual acceptance after migrating the intended database:

1. Sign in, enter a purchase and a later sale, resolve their NBU rates, and POST
   a report for the sale's broker year. Read it via Location and download both formats.
2. Confirm earlier purchases/prior sales participate in FIFO and only the selected
   year's sale matches appear in totals. Check an offset crossing New Year.
3. Correct a trade or split. Read/export the old report unchanged; current-status
   reports changed inputs. Resolve replacement rates and POST a new report.
4. Confirm missing FX blocks creation without partial rows; an empty year returns
   zero totals. Test archived portfolios and another account/non-owner admin.
5. Import CSV financial columns as text and compare all digits with saved JSON.

Automated coverage includes both original spreadsheet calculator cases, annual
saved/exported IBIT reconciliation, prior-year lot consumption, broker/UTC New
Year boundaries, legacy dates, preserved source/split snapshots, full-precision
database round trips, migration upgrade/downgrade protection, account isolation,
CSRF, no partial reports, integrity checks, CSV injection/escaping and concurrent
correction consistency.

Verification on 2026-10-03: tooling/locked restore and Release build passed with
zero warnings/errors; all 241 tests passed (59 unit, 182 PostgreSQL integration,
none skipped). EF reported no pending model changes. Docker Desktop was initially
stopped; it was started and the full suite rerun successfully. Changed-file LF,
local documentation links and whitespace checks passed across all three repositories.
Only disposable test databases were migrated; the user's database was untouched.
No new frontend code, deployment or commit was performed.

Official filing readiness is a separate next step: validate legal scope and
year-specific rules, agree report-level rounding and supported filing/export
formats, and review reconciliation before changing `isTaxReady`. No legal
certification, tax rates, automatic submission, account aggregation, frontend
screens or commits are included in this stage.
