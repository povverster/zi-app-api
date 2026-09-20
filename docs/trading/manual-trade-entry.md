# Instrument catalog and manual trade entry

Implemented on 2026-09-20. This stage records USD stock/ETF buys and sells,
validates chronological holdings, and supports audited replacement corrections.
It does not download NBU rates, produce reports, or provide a frontend screen.

## Authentication and access

All endpoints require a signed-in, active account; mutations require
`X-CSRF-TOKEN`. Follow the [authentication flow](../security/authentication.md).
The shared instrument catalog is readable by all active users, but only a
super administrator can add records. Catalog editing/deletion is not exposed.
Symbols and exchange codes are trimmed and uppercased. The symbol/exchange pair
and a supplied ISIN are unique. Names are trimmed; currency is always USD.
ISIN validation checks 12 ASCII alphanumeric characters, not the official checksum.

Trades and correction history are scoped to the signed-in portfolio owner,
including for super administrators. Valid requests for foreign/missing portfolios
or trades return `404`; disabled sessions return `401`. A regular user's catalog
creation returns `403`. Responses have `Cache-Control: no-store`.

## Endpoints

| Method | Path | Success |
| --- | --- | --- |
| GET | `/api/instruments` | `200`, paginated searchable catalog |
| GET | `/api/instruments/{id}` | `200`, instrument |
| POST | `/api/instruments` | Admin-only `201`, instrument and Location |
| GET | `/api/portfolios/{portfolioId}/trades` | `200`, current trades |
| GET | `/api/portfolios/{portfolioId}/trades/{id}` | `200`, including a superseded original |
| POST | `/api/portfolios/{portfolioId}/trades` | `201`, trade and Location |
| POST | `/api/portfolios/{portfolioId}/trades/{id}/corrections` | `201`, replacement trade and Location |
| GET | `/api/portfolios/{portfolioId}/trades/corrections` | `200`, paginated correction audit |

All lists accept `page` (default 1) and `pageSize` (default 20, maximum 100).
The page must be positive and its offset must fit a signed 32-bit integer.
Responses contain `items`, `page`, `pageSize`, and `totalCount`.
Counts and page items are separate reads, not a snapshot across concurrent edits.

Catalog `search` is an optional literal, case-insensitive substring of symbol,
exchange code, or name (maximum 100 characters); `%` and `_` are not wildcards.
Catalog order is symbol, exchange, then ID. Trade order is execution instant,
FIFO ordering ID, then record ID; audit order is creation instant then ID.
Trade lists exclude superseded records unless `includeSuperseded=true`.

Create an instrument:

```json
{
  "symbol": "TLT",
  "exchangeCode": "NASDAQ",
  "name": "iShares 20+ Year Treasury Bond ETF",
  "type": "Etf",
  "isin": null
}
```

Symbol/exchange limits are 32 characters; name limit is 300. Supported types are
`Stock` and `Etf`. Catalog metadata is manually maintained, not a market-data feed.

Create a trade (replace the instrument placeholder with a catalog UUID):

```json
{
  "instrumentId": "<catalog UUID>",
  "side": "Buy",
  "executedAt": "2025-01-15T16:30:00+02:00",
  "quantity": "2.5",
  "unitPriceUsd": "87.123456789012",
  "feeUsd": "1.25",
  "brokerTransactionId": "broker-123"
}
```

### Precision and source timestamps

`quantity`, `unitPriceUsd`, and `feeUsd` are **JSON strings**, in requests and
responses. Use invariant decimal notation: 1-16 integer digits and optionally
1-12 fractional digits. Quantities/prices must be positive; fees may be zero.
Commas, exponent notation, signs, spaces, JSON numbers, excess scale, and values
outside `numeric(28,12)` are rejected, never silently rounded. Clients must
preserve these strings rather than converting them through JavaScript numbers.

`executedAt` requires an ISO timestamp with seconds, explicit `Z` or numeric
offset, and at most six fractional digits. Future timestamps are rejected.
The server stores both `executedAtUtc` and the original `executedAtOriginal`
string, retaining the submitted offset for future date-policy decisions.
Legacy records have no original string; no historical timezone is fabricated.
An offset is not an IANA broker timezone. The NBU transaction-date policy remains
an explicit decision for the next stage.

### Ownership, duplicates, and validation

IDs are generated UUIDv7. Owner, portfolio, rate, superseded status, and FIFO order
cannot be chosen through extra JSON fields. Only existing USD instruments and
USD portfolios are accepted. The portfolio comes from the authorized URL.

Broker transaction IDs are optional, trimmed, case-sensitive, and at most 200
characters. Blank becomes null. Non-null IDs are unique among current trades in
one portfolio; a duplicate returns `409` even if its payload is identical.
This is rejection, not idempotent replay. Different portfolios can reuse an ID;
omitting IDs does not deduplicate identical-looking trades.

Every mutation replays the current chronological quantities, including stored
split events and all later sales. Overselling, a sale before its purchase, or a
correction that makes a later sale invalid returns `409`. Instruments cannot
borrow holdings from one another. There is no short-selling workflow.
Validation does not invent an FX rate or calculate tax; the existing tax
calculator remains covered by its spreadsheet regression tests.

Trade writes use a database transaction and a portfolio row lock while loading,
validating, and saving. This serializes competing sells/corrections and archive
updates. Future imports and split mutation workflows must coordinate with this
same locking boundary. This stage loads the portfolio ledger for validation;
large-scale import/performance work is deferred. See the
[PostgreSQL locking documentation](https://www.postgresql.org/docs/18/explicit-locking.html)
and [EF transaction documentation](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

### Audited corrections

Send `{"replacement": { ...complete trade payload... }, "reason": "Broker correction"}`
to the original trade's corrections endpoint. A nonblank reason up to 1000
characters is required. The server atomically:

1. Validates the resulting ledger after replacing this trade.
2. Marks the original superseded without changing its source inputs/rate.
3. Creates a new UUIDv7 replacement, initially pending exchange-rate selection.
4. Adds an audit record with original/replacement IDs, acting account, reason,
   and UTC creation time.

The replacement keeps the original `fifoOrderId`; first-version trades use their
own ID. Further corrections keep that key, so price/fee corrections do not change
same-instant ordering. Sorting still uses the execution instant first. Future
FIFO adapters must exclude superseded records and pass canonical `D`-format
`fifoOrderId` to the calculator's optional `FifoOrderId` input. Match IDs remain
the actual replacement IDs, not the ordering key.

The replacement may keep the original broker ID, but cannot take another current
trade's ID. Retrying a correction on a superseded ID returns `409`; read its
audit history and use the current replacement. Reads of the original still work.
Saved tax runs/matches retain their original values and source references;
they are historical snapshots, not recalculated current results. Future reporting
must detect changed inputs and create a new versioned run.

Archived portfolios remain readable but cannot receive creates or corrections
until restored. There is no in-place PUT, hard DELETE, or trade cancellation/void
endpoint. Complete cancellation requires a later audited policy, not deleting rows.

### Errors

Malformed input, invalid precision/time/pagination, missing CSRF, and unsupported
currency return `400`. Business conflicts return `409`. Business error responses
include a stable `code`: `InvalidInput`, `UnsupportedCurrency`, `Duplicate`,
`Archived`, `Oversold`, or `AlreadyCorrected`. Authentication and missing resources
use `401`/`403`/`404`; framework binding/CSRF errors need not include this code.

## Exchange-rate handoff and migrations

New manual entries have `exchangeRateId: null`, `rateStatus: "Pending"`, and
`isTaxReady: false`. Previously linked entries report `LinkedUnverified`; a stored
link alone does not establish the still-unresolved NBU date policy. This API never
marks a trade tax-ready and does not accept a client-selected rate.
The next stage must define date/weekend/missing-rate policy, retrieve dated NBU
rates with provenance, and resolve pending entries without rewriting source data.

Apply **`20260920125916_AddManualTradeEntry`** after the existing three migrations,
using the [README procedure](../../README.md#apply-pending-migrations).
It makes the rate FK nullable, adds original timestamp/FIFO key/superseded state,
backfills legacy FIFO keys from existing IDs, adds correction audit storage, and
changes broker-ID uniqueness to current records. Existing IDs, financial values,
rates, trades, and saved tax matches are preserved. A reset is not required.
Downgrading refuses to erase corrections or replace pending FX links with fake
values; use a forward migration once new data exists. API startup does not migrate.

## Acceptance and verification

With the current migrations applied, authenticate and obtain a fresh CSRF token:

1. As an administrator add a stock/ETF; as a regular user search/read it and verify
   catalog creation is forbidden.
2. Create a portfolio and a buy with fractional quantities/fees. Read it back;
   confirm exact decimal strings, offset/UTC times, and pending rate status.
3. Enter a later partial sale. Reject overselling, earlier sales, duplicates,
   excess decimal precision, timezone-free timestamps, and missing CSRF tokens.
4. Correct a price with a reason. Verify the new ID, retained FIFO key, original
   read, current/all lists, and audit record. Reject a correction that invalidates
   later holdings, or a retry against the superseded original.
5. Repeat reads/writes as another user and as a non-owner administrator: expect
   `404`. Archive the portfolio; reads work, writes return `409`; restore to resume.

Recorded on 2026-09-20: locked restore and local tooling restore succeeded;
Release build passed with zero warnings/errors; 100 tests passed (33 unit and
67 PostgreSQL integration tests), with no skipped tests. Coverage includes
precision/offset preservation, account isolation, CSRF, concurrent sales/duplicates/
corrections, split-aware validation, correction chains, saved source/report
preservation, a legacy-data migration upgrade, and unsafe-downgrade rejection.
EF reported no pending model changes. Tests used disposable databases; the user's
development database was not migrated or reset.
