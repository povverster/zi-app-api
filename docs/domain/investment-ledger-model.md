# Initial investment-ledger model

- Status: Implemented foundation
- Scope: ownership, trades, exchange rates, splits, and reproducible FIFO results

## Ownership boundary

A `UserAccount` represents one person who can sign in. It owns any number of
portfolios. Authentication credentials are intentionally not part of the domain
model; ASP.NET Core Identity now maps credentials to this stable account ID.

The roles are `SuperAdmin` and `User`. Only the authenticated super-admin
provisioning endpoint may create accounts, apart from first-admin bootstrap.
See the [authentication guide](../security/authentication.md).

The [portfolio API](../portfolios/portfolio-management.md) resolves the active
domain account from the current session. Reads and mutations are owner-scoped,
including for super administrators. New portfolios use USD and UUIDv7 IDs.
Portfolio names are trimmed and unique per owner with case-sensitive comparison;
archived names remain reserved. Archive/restore preserves trades and calculation
history. Hard deletion is unavailable; trade creation and correction reject
archived portfolios until restored.

## Main relationships

```text
UserAccount 1 ─── * Portfolio
Portfolio   1 ─── * InvestmentTransaction
Instrument  1 ─── * InvestmentTransaction
Instrument  1 ─── * StockSplit
ExchangeRate 1 ── * InvestmentTransaction
InvestmentTransaction 1 ── 0..1 TradeCorrection (original and replacement roles)
Portfolio   1 ─── * TaxCalculationRun
TaxCalculationRun 1 ── * TaxLotMatchSnapshot
InvestmentTransaction 1 ── * TaxLotMatchSnapshot (purchase and sale roles)
```

Instruments are shared catalog records rather than being duplicated per user.
Portfolio ownership is always resolved through `Portfolio.OwnerAccountId`.

## Immutable source data

Each trade stores the broker execution instant, side, quantity, USD price, USD
fee, instrument, portfolio, optional broker transaction ID, and the exact exchange
rate record when selected. Manual entries preserve their submitted timestamp and
offset alongside UTC; legacy entries have no original timestamp string.
The exchange-rate link is nullable: new manual trades remain pending explicit
NBU resolution rather than receiving a fabricated rate. Select the original broker
calendar date without timezone conversion. The user confirmed older stored broker
dates are already correct too; use those dates unchanged when no original string
exists. Do not infer historical timezones.

The [trade workflow](../trading/manual-trade-entry.md) never edits source values.
A correction marks the original superseded, appends a replacement, and records
the actor, reason, timestamp, and old/new IDs in `trade_corrections`. Replacements
keep the original `FifoOrderId` and start with unresolved FX. Current results must
exclude superseded rows; audit/report snapshots still reference original records.
There is no hard-delete or trade-void endpoint.

An exchange-rate record stores currency, effective date, UAH rate, source, and
retrieval timestamp. New NBU records additionally preserve calculation date,
request URL, exact response text, and SHA-256. They use source `NBU-ExchangeSite-v1`,
distinct from old rows, with an immutable first-response cache.
The [NBU policy](../exchange-rates/nbu-exchange-rates.md) requests the exact calendar
date even on weekends/holidays; a missing rate leaves the trade pending.

Resolution attaches a previously missing rate once and records selected date,
policy version, actor account, and UTC time. Existing links are never overwritten
or silently certified; correction creates a new pending record and retains the
old link/history. Resolved does not mean tax-ready. There is no refresh/backfill job.

## Shared split revisions

Shared `StockSplit` records now have a stable FIFO key, original effective-time
string, actor/time/source provenance, superseded flag and optional previous
revision/reason. Legacy provenance stays null. Admin-only corrections append a
replacement without changing instrument or FIFO key. Current projections exclude
superseded splits; original revisions remain readable. See the
[split contract](../holdings/splits-and-holdings.md).

## Numeric storage

```text
quantity and split ratios     numeric(28, 12)
USD and UAH amounts           numeric(28, 12)
USD-to-UAH rate               numeric(20, 10)
```

The HTTP trade contract uses decimal strings and rejects values that exceed the
storage precision rather than allowing database rounding. The domain continues
calculating with .NET `decimal` and performs no explicit intermediate rounding;
its representable precision still limits nonterminating divisions. The database
scale is a storage boundary, not a reporting rounding rule. V2 holdings return
unrounded decimal strings and do not persist matches. The report stage must
review snapshot precision before writing results beyond 12 fractional places.

## FIFO reproducibility

Every `TaxCalculationRun` identifies its portfolio, tax year, calculation version,
and creation time. Its `TaxLotMatchSnapshot` records the purchase, sale, matched
quantity, source amounts, allocated fees, differences, expenses, and final USD/UAH
profit. This permits a historical result to be audited after calculation rules
change. Corrections do not silently refresh old runs; reporting must detect changed
inputs and create a new versioned run. The FIFO adapter must pass each active
trade's `FifoOrderId` while using its actual ID for match provenance.
The current holdings adapter also passes each active split's stable key and
uses `fifo-uah-v2-remaining-cost`, preserving remaining lot cost/fees across
splits and partial sales. Its historical cutoff replays current revisions, not
the data known at that earlier date. It is not a persisted report or tax-year filter.

The database prevents source trades used by a saved match from being deleted.
Deleting a calculation run may delete only its own derived match snapshots.

## Database invariants

- normalized account email is unique;
- portfolio name is unique within its owner account;
- instrument symbol and exchange pair is unique;
- NBU/source rate is unique by currency and effective date;
- new NBU-source rows require response provenance; a resolution requires all
  four audit fields and a rate link together, with a restricted actor-account FK;
- broker transaction ID is unique among current trades in a portfolio when provided;
- each original trade has at most one correction, each replacement belongs to one
  correction, and audit foreign keys prevent deletion of referenced trades/accounts;
- quantities, prices, exchange rates, and split ratios are positive;
- fees are non-negative;
- a split numerator and denominator cannot be equal;
- split provenance is all-null (legacy) or complete; a correction requires
  previous revision/reason/actor, with restricted previous-split and actor FKs;
- at most one replacement references a split; at most one active split per
  instrument/FIFO ordering key. The API rejects duplicate active effective instants
  under its write lock; old duplicate instants are not removed by migration;
- a tax match is unique for one run, purchase, and sale combination.

## Deferred to later stages

- automated broker import and its format/absolute-instant interpretation;
- audited complete trade cancellation/voiding;
- audited handling of future NBU revisions and optional batch rate resolution;
- dividends, withholding taxes, deposits, withdrawals, and transfers;
- tax-report generation and official filing/display rounding;
- market prices, benchmarks, performance statistics, and S&P 500 comparison.

## Manual-entry migration and concurrency

`AddManualTradeEntry` preserves the initial ledger and backfills FIFO ordering keys
from legacy IDs, makes FX optional, preserves submitted timestamps when available,
and adds superseded status and correction audit records. No table reset is needed.
Rollback refuses to discard correction history or unresolved FX entries.

`AddNbuExchangeRates` adds nullable provenance/resolution columns without
rewriting old rates or trades. Rollback refuses to discard new NBU provenance
or resolution history. Prefer forward migrations; no table reset is required.

`AddSplitManagement` adds nullable source/correction audit and superseded status,
backfills split FIFO keys from legacy IDs, and preserves original ratios/times.
Rollback refuses to erase new provenance or correction history.

Trade writers take a shared transaction-level ledger advisory lock before their
portfolio row lock. Split writers take its exclusive side, then affected portfolio
row locks in ID order, validating every owner including archived portfolios.
This prevents a first trade from appearing after the split writer discovers
affected portfolios. Future quantity-changing imports/voids/transfers must use
the same protocol; old API writers must be drained before upgrading.
Portfolio archive updates participate through the row's update lock.
Rate resolution fetches outside this lock, then rechecks owner/state/date inside
it. Concurrent corrections and archives cannot silently acquire a stale assignment.
Holdings use Repeatable Read for a consistent committed snapshot across their
portfolio/trade/rate/split queries. GET performs no network access or writes.
