# Tax calculation specification: FIFO UAH realized gains

- Calculation version: `fifo-uah-v1`
- Status: Characterized from the supplied spreadsheets
- Reference workbooks: `IBIT_US.xlsx` and `TLT_US.xlsx`

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

Imported timestamps must eventually include the broker timezone and be normalized
to an absolute instant. Until the import format is defined, the golden tests use
UTC placeholders while preserving the spreadsheet ordering.

## Event ordering and FIFO

All purchases, sales, and splits are processed chronologically. Events with the
same timestamp are ordered by stable event ID. A sale consumes the oldest open
purchase lot first. A sale spanning lots creates one match per consumed lot, and
a partially consumed lot remains open with its original FIFO position.

Selling more units than are currently available is rejected. Future purchases
cannot satisfy an earlier sale.

## Split handling

For a split factor `f = numerator / denominator`, every open lot is adjusted as
follows:

```text
adjusted quantity          = quantity × f
adjusted unit cost USD     = unit cost USD ÷ f
adjusted buy fee/unit USD  = buy fee/unit USD ÷ f
```

This preserves the lot's total acquisition cost, total allocated purchase fee,
and FIFO position. Closed quantities are not changed.

## Calculation for one FIFO match

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

The supplied workbooks have split factor `1` for every purchase, so they do not
contain a real split example. The automated tests add a synthetic 5-for-1 split
that verifies quantity adjustment and preservation of total purchase cost.

## Precision, rounding, and reproducibility

All quantities, money, fees, rates, and intermediate results use base-10 decimal
arithmetic. The calculator does not round intermediate values. A later reporting
specification must define display and filing rounding independently; rounded
display values must never replace stored source values or calculation results.

A generated report must eventually store its calculation version, input event
IDs, FIFO matches, exchange-rate records, and unrounded results so that it can be
reproduced after business rules evolve.

## Decisions still required before filing-ready reports

- broker timestamp timezone and same-timestamp import ordering;
- NBU rate selection for weekends, holidays, and unavailable dates;
- the official report-level rounding rules;
- treatment of non-trading charges, withholding tax, dividends, and transfers;
- legal review of the calculation and generated Ukrainian tax-report format.
