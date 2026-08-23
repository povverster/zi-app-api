# ADR 0001: FIFO tax-lot matching

- Status: Accepted
- Date: 2026-08-23

## Context

ZiApp must reproduce the user's current spreadsheet workflow for realized gains
and Ukrainian tax reporting. A sale can consume all or part of several earlier
purchases, and corporate actions such as stock splits can change quantities
without changing economic ownership.

## Decision

ZiApp will use FIFO (first in, first out) as the mandatory tax-lot matching method.

- Purchase lots are ordered by execution timestamp and then by a stable transaction ID.
- A sale consumes the oldest available quantity first.
- Partial fills and sales spanning multiple lots produce explicit match records.
- A stock or ETF split adjusts lot quantity and per-unit cost while preserving
  acquisition order and total cost.
- A transfer between portfolios or brokers preserves the original acquisition data.
- Generated reports retain their lot matches and calculation-version metadata so
  historical results remain reproducible.
- FIFO is not user-selectable. Calculation rules are versioned so a future legal or
  product change can coexist with reports generated under an older version.

## Consequences

The transaction model must keep immutable source values, precise decimal amounts,
event ordering, and an audit trail. FIFO logic will live in the domain/application
layers and will be covered by spreadsheet-derived golden test cases before tax
report generation is implemented.
