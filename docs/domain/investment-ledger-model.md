# Initial investment-ledger model

- Status: Implemented foundation
- Scope: ownership, trades, exchange rates, splits, and reproducible FIFO results

## Ownership boundary

A `UserAccount` represents one person who can sign in. It owns any number of
portfolios. Authentication credentials are intentionally not part of the domain
model; the authentication stage will map an identity to this stable account ID.

The initial roles are `SuperAdmin` and `User`. Only the future administration
application service may create accounts. Storing the role is necessary but does
not itself enforce that authorization rule.

## Main relationships

```text
UserAccount 1 ─── * Portfolio
Portfolio   1 ─── * InvestmentTransaction
Instrument  1 ─── * InvestmentTransaction
Instrument  1 ─── * StockSplit
ExchangeRate 1 ── * InvestmentTransaction
Portfolio   1 ─── * TaxCalculationRun
TaxCalculationRun 1 ── * TaxLotMatchSnapshot
InvestmentTransaction 1 ── * TaxLotMatchSnapshot (purchase and sale roles)
```

Instruments are shared catalog records rather than being duplicated per user.
Portfolio ownership is always resolved through `Portfolio.OwnerAccountId`.

## Immutable source data

Each trade stores the broker execution instant, side, quantity, USD price, USD
fee, instrument, portfolio, optional broker transaction ID, and the exact exchange
rate record selected for it. Source trade values are not edited after import;
corrections will be modeled as an audited application workflow later.

An exchange-rate record stores currency, effective date, UAH rate, source, and
retrieval timestamp. The initial production source will be the National Bank of
Ukraine. Weekend and holiday selection rules remain part of the later NBU import
specification.

## Numeric storage

```text
quantity and split ratios     numeric(28, 12)
USD and UAH amounts           numeric(28, 12)
USD-to-UAH rate               numeric(20, 10)
```

The domain continues calculating with .NET `decimal` and does not round
intermediate results. The database scale is a storage boundary, not a reporting
rounding rule.

## FIFO reproducibility

Every `TaxCalculationRun` identifies its portfolio, tax year, calculation version,
and creation time. Its `TaxLotMatchSnapshot` records the purchase, sale, matched
quantity, source amounts, allocated fees, differences, expenses, and final USD/UAH
profit. This permits a historical result to be audited after calculation rules
change.

The database prevents source trades used by a saved match from being deleted.
Deleting a calculation run may delete only its own derived match snapshots.

## Database invariants

- normalized account email is unique;
- portfolio name is unique within its owner account;
- instrument symbol and exchange pair is unique;
- NBU/source rate is unique by currency and effective date;
- broker transaction ID is unique within a portfolio when provided;
- quantities, prices, exchange rates, and split ratios are positive;
- fees are non-negative;
- a split numerator and denominator cannot be equal;
- a tax match is unique for one run, purchase, and sale combination.

## Deferred to later stages

- ASP.NET Core Identity credentials, sessions, and super-admin endpoints;
- broker import and duplicate detection policies;
- NBU downloading, weekend/holiday fallback, and provenance payloads;
- dividends, withholding taxes, deposits, withdrawals, and transfers;
- tax-report generation and official filing/display rounding;
- market prices, benchmarks, performance statistics, and S&P 500 comparison.
