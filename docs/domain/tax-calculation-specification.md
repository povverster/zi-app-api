# Tax calculation specification: FIFO UAH realized gains

- Original match calculation: `fifo-uah-v1`
- Current holdings and saved draft reports: `fifo-uah-v2-remaining-cost`
- Status: Characterized from the supplied spreadsheets
- Original reference workbooks: `IBIT_US.xlsx` and `TLT_US.xlsx`
- Additional samples reviewed on 2026-10-03: see the
  [sample audit](spreadsheet-sample-audit.md). They are not all automated fixtures.

## Purpose and scope

This specification records the calculation behavior that ZiApp must reproduce.
It covers FIFO tax-lot matching, transaction fees, USD-to-UAH conversion, and
stock splits. The calculator is invoked for one account, portfolio, and
instrument at a time.

This is a software specification derived from the supplied workbooks. It is not
a legal conclusion that the workbook method satisfies current Ukrainian tax law.
That question must be validated separately before generated reports are treated
as filing-ready.

## Required immutable inputs

Every purchase and sale keeps:

- a stable unique event ID;
- the broker execution timestamp;
- the executed quantity;
- the USD unit price;
- the total broker fee in USD; and
- the official USD-to-UAH rate selected for that transaction date.

Every split keeps its stable ID, effective timestamp, numerator, and denominator.
For example, a 5-for-1 split has numerator `5` and denominator `1`.

Manual-entry records may exist with unresolved exchange rates; these are not
valid tax-calculation inputs and must not be supplied to the calculator with
placeholder rates. The [trade API](../trading/manual-trade-entry.md) preserves
submitted numeric timezone offsets and normalized UTC. The user confirmed that
all broker calendar dates are already correct, including old records: do not
convert them for NBU selection. Use the original timestamp's date when available;
otherwise use the stored date unchanged. The
[NBU integration guide](../exchange-rates/nbu-exchange-rates.md) defines both
versioned policies, exact-date weekend/holiday lookup, and missing-rate rejection.
Resolved rates still do not establish filing readiness.

Future broker imports must preserve the broker calendar date and establish an
absolute instant for ordering from documented source offsets/timezones; do not
shift the rate date during normalization. Import formats are still undefined.
The spreadsheet golden tests use UTC placeholders while preserving sheet ordering.

## Event ordering and FIFO

All purchases, sales, and splits are processed chronologically. Events with the
same timestamp are ordered by stable event ID. Audited trade and split replacements retain
their original `FifoOrderId` for this tie-breaker (the calculator accepts it as an
optional input, defaulting to the record ID for existing callers). Only current,
nonsuperseded trades and splits participate. The adapter uses canonical D-format
UUID strings and ordinal comparison, with actual record ID as the final
tie-breaker. Match records still use actual source record IDs.
A sale consumes the oldest open
purchase lot first. A sale spanning lots creates one match per consumed lot, and
a partially consumed lot remains open with its original FIFO position.

Selling more units than are currently available is rejected. Future purchases
cannot satisfy an earlier sale.

## Split handling

The original v1 calculator uses a split factor `f = numerator / denominator`
and adjusts each lot as follows:

```text
adjusted quantity          = quantity × f
adjusted unit cost USD     = unit cost USD ÷ f
adjusted buy fee/unit USD  = buy fee/unit USD ÷ f
```

This is intended to preserve total acquisition cost, allocated purchase fee,
and FIFO position, but a nonterminating factor can accumulate decimal residuals.
V2 below tracks remaining totals directly instead. Neither version rewrites
source trades or already produced match records.

## Original v1 calculation for one FIFO match

Let:

```text
q       = matched quantity
buyUsd  = split-adjusted purchase unit price in USD
sellUsd = sale unit price in USD
buyFee  = split-adjusted purchase fee per unit in USD
sellFee = total sale fee USD / total sale quantity
buyFx   = USD-to-UAH rate for the purchase date
sellFx  = USD-to-UAH rate for the sale date
```

Calculate:

```text
purchase cost USD   = q × buyUsd
sale proceeds USD   = q × sellUsd
purchase fee USD    = q × buyFee
sale fee USD        = q × sellFee

purchase cost UAH   = purchase cost USD × buyFx
sale proceeds UAH   = sale proceeds USD × sellFx
purchase fee UAH    = purchase fee USD × buyFx
sale fee UAH        = sale fee USD × sellFx

gross difference USD = sale proceeds USD - purchase cost USD
gross difference UAH = sale proceeds UAH - purchase cost UAH
expenses USD         = purchase fee USD + sale fee USD
expenses UAH         = purchase fee UAH + sale fee UAH
profit USD           = gross difference USD - expenses USD
profit UAH           = gross difference UAH - expenses UAH
```

UAH profit is not USD profit multiplied by a single exchange rate. Purchase
amounts and purchase fees use the purchase-date rate; sale amounts and sale fees
use the sale-date rate.

## V2 holdings: remaining-cost allocation

`CalculateHoldings` identifies itself as `fifo-uah-v2-remaining-cost`.
It begins each lot with quantity, quantity times unit price as remaining USD
cost, and total USD fee. A split applies
`remainingQuantity * numerator / denominator` without changing remaining cost
or fee. Multiplying first avoids turning 3 units in a 1-for-3 split into
0.999... units. FIFO lot order and purchase-date FX stay unchanged.

For a partial match, allocate `remainingCost * q / remainingQuantity` and
`remainingFee * q / remainingQuantity`. For final lot consumption, take the
entire remaining cost/fee rather than multiplying rounded per-unit values.
Subtract allocated amounts from that lot. Sale fees use the same remaining-fee
allocation across matches, with the last match taking the remainder.
Sale proceeds and independent USD/UAH conversions follow the formulas above.
Open lots expose remaining quantity/cost/fee and purchase FX provenance.

This is a precision-policy change and therefore has a new version. The original
`Calculate` entry point preserves v1 match behavior and does not expose valued
open lots. The IBIT/TLT examples are tested under both versions. Saved historical
reports are not upgraded or rewritten by the holdings endpoint.

The [holdings workflow](../holdings/splits-and-holdings.md) does not pass unresolved
or unverified FX into this calculator: quantities remain available, but the
instrument's financial results and portfolio-wide totals are withheld. A cutoff
uses current revisions effective through that instant, not what was known then.

## Spreadsheet traceability

The supplied sheets expand purchase and sale batches into unit rows. ZiApp keeps
the batches and creates explicit FIFO matches instead. These are equivalent
because fees are allocated pro rata by quantity.

| Spreadsheet columns | Meaning |
| --- | --- |
| A-M | purchase date, rate, price, matched quantity, costs, fees, batch quantity, split factor |
| O-W | sale date, rate, price, quantity, proceeds, and sale fees |
| X-Y | gross difference in USD and UAH |
| Z-AA | total allocated expenses in USD and UAH |
| AB-AC | net realized profit in USD and UAH |
| AD | sale batch quantity used to allocate its fee |

## Golden examples

| Workbook | Sold units | Gross USD | Gross UAH | Expenses USD | Expenses UAH | Profit USD | Profit UAH |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| IBIT | 10 | -181.800000 | -7166.046794 | 8.400000 | 355.568540 | -190.200000 | -7521.615334 |
| TLT | 41 | -4.290000 | 2490.099310 | 51.580000 | 2146.148824 | -55.870000 | 343.950486 |

The TLT case is an important regression check: its USD result is negative while
its UAH result is positive because the transaction-date exchange rates differ.

The original IBIT/TLT workbooks have split factor `1` for every purchase, so those
two do not contain a real split example. The automated tests add a synthetic
5-for-1 split that verifies quantity adjustment and preservation of total cost.
The newly supplied SCHD sample includes factor `3` and prior-year sales; its
split-event provenance and batch reconstruction still need verification before
adding an end-to-end fixture. See the sample audit, including the user's BXMT
purchase-price correction to `18.08 USD`.

## Precision, rounding, and reproducibility

All quantities, money, fees, rates, and intermediate results use base-10 decimal
arithmetic. There is no explicit intermediate rounding, but .NET decimal has
finite precision and cannot represent every fraction exactly. V2 preserves
remaining USD cost/fee totals through split events and allocates final remainders.
Overflow or a positive split quantity rounded down to zero is rejected in v2,
not silently treated as a zero holding. A later reporting
specification must define display and filing rounding independently; rounded
display values must never replace stored source values or calculation results.

The [saved draft report contract](../reports/draft-tax-reports.md), implemented
2026-10-03, stores calculation/year/precision versions, exact input trade/split/rate
revisions and provenance, replay cutoff, FIFO matches and unrounded results.
Calculated match columns use unconstrained PostgreSQL numeric, avoiding a second
rounding to 12 fractional places. Source input precision is unchanged.

The user chose one portfolio per annual report. Sales are selected by the original
broker calendar year (legacy dates unchanged); preceding trades and splits replay
in UTC order to consume earlier FIFO lots. Only selected-year sales enter totals.
Saved JSON is authoritative and immutable through the API; current-input comparison
is separate from reads/exports. CSV/JSON retain full calculated precision.
Policy `full-decimal-no-filing-rounding-v1` deliberately does not specify final
filing rounding, tax rates/payable or legal loss treatment. Reports remain drafts.

## Decisions still required before filing-ready reports

The user's 2026-10-04 clarification separates those future official-filing
decisions from the implemented [user-configured report](../reports/configured-tax-reports.md).
That report retains the signed annual net result and a negative loss field,
uses max(net, 0) only as the percentage-calculation base, applies explicitly
selected same-year rates, and rounds each final tax to two places half away
from zero. A loss of -10000 stays visible while taxes are 0.00. The original
FIFO/full-precision snapshots are unchanged. Dividends and carryforward
deductions remain deferred; specialist review is not a blocker for configurable
arithmetic. Its rounding convention is not asserted to be official form policy.

The [2025 filing-readiness review](../reports/ua-2025-filing-readiness.md) is the
current record of official-source findings and unresolved review gates.
The user confirmed 2025, Ukrainian tax-resident personal foreign-broker stock/ETF
sales and a separate annual summary. These choices do not legally validate the
current draft calculation, fee deductions or FIFO scope. One-portfolio snapshots
stay unchanged; external inputs and prior-loss claims belong in the separate workflow.

- broker import formats, source offset interpretation for absolute ordering, and
  deterministic same-timestamp import IDs (without shifting broker rate dates);
- the official report-level rounding rules;
- treatment of non-trading charges, withholding tax, dividends, and transfers;
- legal review of the calculation and generated Ukrainian tax-report format.
