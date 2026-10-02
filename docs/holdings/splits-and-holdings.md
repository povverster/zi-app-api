# Split management and holdings/FIFO results

Implemented on 2026-10-02. This stage adds shared corporate-action maintenance
and owner-scoped current/historical holdings projections. It does not generate
saved tax reports or certify Ukrainian filing readiness.

## Access and endpoints

All endpoints require an active signed-in account and return no-store responses.
Follow the [cookie/CSRF flow](../security/authentication.md). Split reads are
shared catalog data. Only a super administrator may create/correct a split, with
a valid `X-CSRF-TOKEN`. Holdings are owner-only, even for super administrators.
Archived portfolios remain readable and participate in global split validation.

| Method | Path | Result |
| --- | --- | --- |
| GET | `/api/instruments/{instrumentId}/splits` | 200, paginated current splits |
| GET | `/api/instruments/{instrumentId}/splits/{id}` | 200, including superseded revisions |
| POST | `/api/instruments/{instrumentId}/splits` | Admin-only 201, split and Location |
| POST | `/api/instruments/{instrumentId}/splits/{id}/corrections` | Admin-only 201, replacement and Location |
| GET | `/api/portfolios/{portfolioId}/holdings` | 200, quantities, open lots, FIFO matches and realized totals |

Split lists use `page=1`, `pageSize=20` (maximum 100), and
`includeSuperseded=false`. Response: `items`, `page`, `pageSize`, `totalCount`.
Order: effective UTC instant, FIFO ordering key, then record ID. Counts and items
are separate reads; pagination is not a snapshot across concurrent corrections.

## Record or correct a split

Example 2-for-1 split:

```json
{
  "effectiveAt": "2025-01-02T14:00:00+02:00",
  "numerator": "2",
  "denominator": "1",
  "sourceReference": "Broker corporate-action notice"
}
```

For a 1-for-3 reverse split, use numerator `"1"` and denominator `"3"`.
Both are positive invariant decimal JSON strings fitting numeric(28,12):
1-16 integer digits, optionally 1-12 fractional digits, no exponent/comma/sign.
The ratio must change the number of units. No silent input rounding is allowed.
Effective timestamps require seconds and an explicit offset or Z, with at most
six fractional digits. Future events are not accepted. The original timestamp
and offset are retained alongside UTC.

The source reference is required, trimmed, and at most 1000 characters. It may
identify an official notice, but is stored as text, not fetched or verified.
Render it safely as text in clients. Splits are global to the shared instrument,
not one user's portfolio, and currently support USD instruments only.

Correction payload:

```json
{
  "replacement": {
    "effectiveAt": "2025-01-02T14:00:00+02:00",
    "numerator": "3",
    "denominator": "1",
    "sourceReference": "Corrected broker notice"
  },
  "reason": "Corrected official ratio"
}
```

A correction stays on the same instrument. A nonblank reason up to 1000
characters is required. It marks the original superseded and appends a UUIDv7
replacement in one transaction, preserving original values, actor/time/source,
and the original `fifoOrderId`. The replacement records `previousSplitId`
and `correctionReason`. Every revision remains readable; list with
`includeSuperseded=true` for history. Legacy split provenance remains null.

New duplicate active effective instants return 409, even for identical payloads;
this is not idempotent replay. Existing legacy duplicates are not erased.
Retrying correction of a superseded ID also returns 409. There is no in-place
edit, deletion, cancellation, or automatic cash-in-lieu workflow. Fractional
holdings are retained. Wrong-instrument entries cannot currently be cancelled;
validate shared catalog selection carefully.

Every split change replays affected holdings across all accounts, including
archived portfolios. If a later sale would become invalid, no revision is saved.
Errors do not expose another owner's portfolio IDs. Source trades, their rate
assignments, and saved tax-report snapshots are never rewritten by a split.

## Holdings response and cutoffs

Optional `asOf` is an ISO timestamp with the same validation as effective times.
Encode a literal plus sign as `%2B` in a query, e.g.
`?asOf=2025-01-01T14%3A00%3A00%2B02%3A00`.
Omitting it uses current UTC. The cutoff is inclusive.

The response contains `portfolioId`, `asOfUtc`, `calculationVersion`,
`isComplete`, `isTaxReady`, `positions`, and `realizedTotals`.
Each position contains instrument metadata, quantity, status, rate blockers,
`splitIds`, open lots, realized matches, and realized totals.

- Quantities are available even when rates are unresolved.
- `Complete` positions have open lots with remaining purchase cost and fees
  in USD/UAH, plus FIFO matches and gain totals. Source trade and rate IDs are
  included. Cost fields exclude fees, which are separate.
- Any unresolved or unverified trade makes its instrument `PendingRates`.
  Its `rateBlockers` identify trades and Pending/LinkedUnverified status.
  Its open lots, matches, and totals are null, not fabricated zeros.
- Other complete instruments may still show their own results, but portfolio
  totals are null until every position is complete.
- Empty portfolios have no positions and complete zero realized totals.
  Closed positions remain present to retain their realized matches.
- `splitIds` identifies the current split inputs at or before the cutoff,
  including events before the first purchase that have no effect on a lot.
- All financial quantities/amounts are invariant decimal strings, including
  calculated fractional values (up to .NET decimal's 28 fractional places).
  Keep these as strings in JavaScript; do not recalculate with binary floats.
- `isTaxReady` is always false, even when `isComplete` is true.

A complete position requires every included trade to have a verified NBU
assignment matching its broker-date policy, selected date, actor/time, and
response provenance. GET does not fetch NBU rates, attach rates, or save reports.
Use the explicit [NBU resolution workflow](../exchange-rates/nbu-exchange-rates.md).
Broker rate dates remain unchanged, including older records.

Historical `asOf` recalculates the current nonsuperseded revisions whose effective
times are at/before the cutoff. It is NOT a reconstruction of what was recorded
or known on that date. Later corrections can change a historical projection.
Saved report snapshots are separate and remain unchanged. Realized totals cover
all sales through the cutoff, not a selected tax year or account-wide aggregation.

## Versioned calculation and concurrency

Holdings use `fifo-uah-v2-remaining-cost`. It maintains remaining USD acquisition
cost/fees per lot. Splits change only remaining quantity; partial sales allocate
cost/fees proportionally, and final consumption takes the remaining amount.
A sale fee is similarly exhausted across its matches. Quantity adjustment
multiplies by numerator before dividing by denominator: three units in a
1-for-3 split become exactly one, not a rounded factor times three.

Chronology uses the broker execution/effective UTC instant, then canonical
D-format `fifoOrderId` with ordinal comparison, then record ID. Corrections retain
their ordering key. Purchase costs/fees use purchase FX; sale proceeds/fees use
sale FX. V1 `Calculate` remains available for original match calculations;
only `CalculateHoldings` exposes open lots. See the
[calculation specification](../domain/tax-calculation-specification.md).

No explicit intermediate or filing/display rounding is performed. Decimal has
finite precision; nonterminating divisions use its representable precision.
Overflow, or a split that reduces a positive lot below representable precision
to zero, fails rather than inventing a result. Very large inputs may be valid
storage values but have unrepresentable computed products.

All quantity-changing writers must acquire the transaction-level ledger advisory
lock BEFORE portfolio row locks. Trades use its shared side; splits use its
exclusive side, then lock affected portfolios in ID order. This also serializes
a split against a portfolio's first trade, before it appears in the affected set.
Unrelated trade writers can still proceed concurrently, while a global split
write briefly blocks all trade writes. Future import/transfer/cancellation
writers must follow the same protocol.

Holdings load their trades, splits, and rates in one Repeatable Read transaction
so concurrent changes cannot produce a mixed committed snapshot. See PostgreSQL
[locking](https://www.postgresql.org/docs/18/explicit-locking.html) and
[transaction isolation](https://www.postgresql.org/docs/18/transaction-iso.html).
This stage loads full ledgers and returns all matches/open lots without pagination.
Large-portfolio projection/storage optimization is deferred.

## Errors

- 400: malformed precision/timestamp/source/reason/pagination or missing CSRF;
  business codes `InvalidInput` or `UnsupportedCurrency`.
- 401: anonymous or inactive account.
- 403: a non-admin tries to mutate shared splits.
- 404: missing catalog/split or a missing/foreign portfolio.
- 409: `Duplicate`, `AlreadyCorrected`, or `InvalidLedger` (overselling or
  unrepresentable calculation). Split-validation arithmetic overflow returns
  `InvalidInput` instead. Framework/CSRF failures need not include a business code.

## Migration and acceptance

Apply **`20261002125349_AddSplitManagement`** after `AddNbuExchangeRates`
using the [migration procedure](../../README.md#apply-pending-migrations).
It adds split ordering/correction/provenance fields, backfills existing ordering
keys from their original IDs, and keeps legacy source/actor/time fields null.
It preserves existing trades, ratios, rates, and saved reports. No reset is needed.
Downgrade refuses to discard new split provenance or correction history.

Stop/drain old API writers before deploying this stage: older instances do not
participate in the new ledger advisory lock. Apply the forward migration and
run only writers using the new locking protocol. Startup does not migrate.
This task's tests migrated disposable databases, not the user's development DB.

Manual acceptance after applying migrations to the intended database:

1. Log in, create a portfolio and buy, then read holdings: quantity present,
   PendingRates, null financial results. Resolve the rate explicitly.
2. Add a later sale, resolve its rate, and inspect remaining lots and FIFO matches.
3. As super admin add a split between buy/sale; confirm all affected portfolios
   recalculate, while original trade quantities and rates remain unchanged.
4. Correct the split with a reason; compare current/history reads, stable ordering
   key, and old source inputs. Reject a reverse split that invalidates later sales.
5. Read with an earlier cutoff; test another account (404), missing CSRF (400),
   non-admin writes (403), and an archived owner's portfolio (readable).

Automated coverage includes spreadsheet regressions under both versions, partial
and multiple lots, reverse/repeated splits, exact final cost/fee allocation,
stable same-instant ordering, rate blockers, historical cutoffs, archived/private
portfolios, source/report preservation, rejected corrections, concurrent writes,
legacy migration preservation, and unsafe-downgrade protection.

Verification on 2026-10-02: tooling/locked restore and Release build passed with
zero warnings/errors; all 206 tests passed (49 unit, 157 PostgreSQL integration,
none skipped), including the deterministic first-trade lock test. EF reported no
pending model changes. Changed-file LF, local documentation links and whitespace
checks passed across all three repositories. No user-database migration/reset,
frontend implementation, deployment or commit was performed.

The next backend stage is saved versioned tax reports. Scope, input snapshots,
export/reconciliation and report rounding must be agreed there; current results
are not filing-ready. Prices, unrealized gains, performance benchmarks and UI
screens are not implemented by this stage.
