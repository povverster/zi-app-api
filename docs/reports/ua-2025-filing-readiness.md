# Ukrainian 2025 filing-readiness review

## Product clarification, 2026-10-04: not a blocker for configurable reports

The user clarified that the immediate goal is a spreadsheet-based calculation
tool using configurable rates for a selected year, not an officially validated
declaration form. The [configured-report contract](configured-tax-reports.md)
now implements that scope, preserving signed losses and zero taxes on losses.
The preceding review-package request was superseded by this clarification.
Do not require a specialist review or keep repeating research before allowing
user-configured calculations. The legal gates below concern **official statutory
policy/form support only**; they remain open without blocking the product roadmap.
The historical research statements below describe their original bounded stage.

Research and product decisions recorded on 2026-10-03.
Status: **research baseline complete; legal validation and filing implementation
remain open**. This document is not a tax opinion or an official declaration.
No runtime behavior, migration, tax payable or readiness flag changes in this stage.

## Confirmed product scope

- First target: income year **2025**, with filing in 2026. This does not approve
  rules for 2024, 2026 or any other year.
- Ukrainian tax-resident individuals investing personally through foreign brokers,
  initially stock/ETF sales. Dividends, derivatives and business activity are excluded.
  ETF labels alone do not establish the legal classification of every fund/trust.
- FIFO remains the user's selected matching method. Its legal applicability,
  identical-asset grouping and treatment across brokers still need review.
- Existing [one-portfolio/year drafts](draft-tax-reports.md) remain unchanged.
- The user approved a **separate taxpayer-year summary** covering relevant
  portfolios, prior losses and investments outside the app. It must not replace
  or merge the portfolio reports.
- Broker calendar dates remain as recorded, including legacy records. No timezone
  conversion is authorized. Determining which broker event a future filing rule
  requires is separate from changing the calendar date of that event.

The first implementation should remain an annual **preparation draft**, not a
complete personal declaration. Unsupported activity or incomplete coverage must
be visible, never silently ignored.

## Official-source findings and limits

Sources below were consulted on 2026-10-03. Legislative requirements, explanatory
articles and software design decisions are distinct. A current webpage does not
prove the exact consolidated legislation or form applicable to a historical year.

| Topic | Finding | Consequence for this app |
| --- | --- | --- |
| Rates | DPS explicitly lists 18% general PIT and 5% military levy for 2025, subject to exceptions. [2026 campaign rates](https://www.tax.gov.ua/deklaratsiyna-kampaniya-2026/stavki-podatku-na-dohodi-fizichnih-osib-ta-viyskovogo-zboru) | Record separately by tax year. Known rates alone do not certify a statutory tax base or form. The later user-configured calculator explicitly captures selected rates and is not statutory certification. |
| Effective period | DPS distinguishes annual 2024 income from annual 2025 income for military levy. [Transition guidance](https://tax.gov.ua/nove-pro-podatki--novini-/856165.html) | A historical report must never inherit today's default rate. |
| Annual result | Sections 170.2.1/170.2.6 describe taxpayer-level annual investment results, eligible gain/loss netting and negative-result carryforward. [Tax Code, official DPS mirror](https://tax.gov.ua/nk/rozdil-iv--podatok-na-dohodi-fizichnih-o/) | Portfolio totals are inputs to preparation, not separate final tax liabilities. Prior-loss eligibility cannot be inferred from an old draft's negative total. |
| Evidence and restrictions | Sections 170.2.2, 170.2.4-170.2.8 address documented acquisition costs, loss restrictions, exchange-related distinctions/exceptions and exemptions. [Tax Code](https://tax.gov.ua/nk/rozdil-iv--podatok-na-dohodi-fizichnih-o/) | Require evidence and reviewed classification. Listing exchange, execution venue and eligibility are not interchangeable. Do not mechanically import a US wash-sale rule. |
| Partial disposals | DPS describes proportional documented acquisition costs for partial disposals. [Kyiv guidance](https://kyiv.tax.gov.ua/media-ark/news-ark/923065.html) | This supports testing partial allocation; it does not independently validate the chosen FIFO lot-selection method. |
| FX timing | DPS discusses conversion at income receipt for foreign investment profit. [Zaporizhzhia guidance](https://zp.tax.gov.ua/media-ark/news-ark/797858.html) | Validate execution versus settlement/receipt, acquisition FX, currency differences and fee dates. This article alone does not settle the app's complete two-date algorithm. |
| Form family | DPS identifies appendix F1 for foreign investment profits and refers to main-declaration row 10.8. [Vinnytsia guidance, 2025-01-31](https://vin.tax.gov.ua/media-ark/news-ark/865243.html) | F1 is a research starting point. Row numbers are not an approved field mapping for the filing-time form. |
| Form revision | The registry lists a declaration effective 2026-01-01. [Official forms registry](https://www.tax.gov.ua/zakonodavstvo/elektronni-formi-dokumentiv/podatok-na-dohodi-fizichnih-osib/) | Obtain and pin the actual applicable declaration, F1 and electronic schema before implementing exports. |
| Monetary presentation | DPS describes hryvnias with kopecks for monetary fields. [Rivne instructions](https://rv.tax.gov.ua/media-ark/news-ark/854148.html) | Two decimal places do not establish midpoint rounding or whether rounding occurs per match, line, annual base or tax. Those remain open. |

The Code's exemption threshold refers to annual income from disposals, not simply
per-portfolio profit (section 170.2.8). Do not configure a numeric threshold until its
2025 value and applicability are independently pinned.

### Access and version limitations

- The DPS Code mirror was readable, but the Rada historical consolidation for
  2025-12-31 could not be retrieved. A year-specific legislative audit is incomplete.
- The registry's [2026 declaration workbook](https://www.tax.gov.ua/data/material/000/054/90753/Podatkova_deklarats_2026.xls)
  could not be downloaded (access denied). Its worksheets/cells and schema were
  **not inspected**; no mapping or template checksum has been verified.
- An [Odesa article dated 2025-04-30](https://od.tax.gov.ua/deklaratsiyna-kampaniya-2025/informatsiyni-povidomlennya/892466.html)
  refers to an older form/row 10.5, unlike the Vinnytsia article above.
  Publication date alone is not a reliable form-version selector.
- The supplied calculation workbooks are user examples, not official declaration
  templates or broker evidence. Their agreement cannot close these legal gaps.

## Review gates before official calculations or exports

Obtain a Ukrainian tax specialist's documented review of these questions against
the legislation and form applicable to 2025. User approval of product scope is
not a substitute for validating the rules.

1. **Coverage and classification:** define the supported legal asset categories,
   exchange/non-exchange distinctions, exemption treatment and required evidence.
   An instrument's `ExchangeCode` is not proof of each trade's execution venue.
   Unsupported assets/activity keep the summary non-filing-ready.
2. **Losses:** determine applicable restrictions/exceptions, cross-category netting,
   eligible prior-year balances and evidence, including military-levy treatment.
   Keep claims separate from accepted balances; avoid deducting the same loss twice.
3. **Dates and FX:** work through actual broker execution, settlement and receipt
   records, including a year boundary and both directions of exchange-rate change.
   Preserve original dates and rate provenance. Any new filing-date policy must
   be separately versioned, not retroactively substituted into saved drafts.
4. **Fees and FIFO:** review purchase/sale commissions, custody/subscription/FX
   charges, identical-asset scope, allocation across brokers and transfers.
   Current draft fee deductions remain software behavior, not approved tax treatment.
5. **Rounding:** specify the rounding mode, stage, treatment of negative values,
   line-to-total reconciliation and PIT/levy calculations independently.
   Approve examples around `1.005`, `-1.005` and repeated fractional lot allocations.
6. **Forms and withholding:** inspect the applicable main declaration and F1,
   row/column transfer rules, withholding/foreign-tax-credit handling and schema
   validation. Do not infer that a limited investment worksheet is a complete return.
   Filing, signatures, amended returns and submission are outside this stage.

Retain the existing `fifo-uah-v2-remaining-cost` and
`full-decimal-no-filing-rounding-v1` policies. Never relabel historical draft
snapshots as validated. A future legal-policy version requires reviewed fixtures
and explicit user confirmation before official tax/form implementation.

## Annual preparation draft implementation brief

This brief was implemented as the bounded backend stage completed on 2026-10-04.
The [annual preparation contract](annual-preparation-drafts.md) now defines the
endpoints, limits, migration and tests. The brief below preserves its design
intent; it is not evidence that the legal review gates above have been closed.

### Scope and preserved inputs

- Owner-only, one signed-in domain account representing one taxpayer and year 2025.
  Do not combine different accounts, even for a super administrator. Do not collect
  tax identifiers or other sensitive identity data merely to create a draft.
- Select explicit immutable portfolio-report IDs, at most one per portfolio/year.
  Support owned archived portfolios. Require complete, hash-verified snapshots;
  reject foreign ownership, wrong year, duplicates and legacy snapshot-less runs.
  Do not auto-select "latest" reports or overwrite them.
- Record coverage of all relevant portfolios/broker accounts and external activity.
  Missing/unknown, confirmed none and supplied data are different states.
  Show omitted in-app portfolios for review without inferring their tax relevance.
- Save externally supplied annual amounts as separately labeled unverified inputs
  with source reference and declared coverage. Record prior-loss claims with origin
  year, amount, previously used amount and evidence reference. Do not apply them
  to a tax base or mark them accepted in this stage.
- Evidence references are text, not automatic downloads. Do not fetch arbitrary
  user URLs or introduce statement uploads in this stage.
- Detect repeated source trade IDs and reject known overlap. Broker IDs are only
  reliable with broker/account context: matching timestamps/amounts are warnings,
  not proof that distinct executions are duplicates. Require coverage/overlap
  confirmation; do not claim undetectable external duplicates are ruled out.
- Preserve the selected report IDs/hashes, source policy versions, inputs, claims,
  confirmations and creation actor/time in an immutable annual snapshot. A new
  selection produces a new draft; do not change old portfolio or annual snapshots.
  Use UUIDv7 and exact decimal strings, consistent with existing persistence rules.

### Outputs and boundaries

- Expose selected-report subtotals, separately labeled external inputs, evidence/
  coverage issues and unverified prior-loss claims. A subtotal is a draft arithmetic
  result, not eligible taxable income. Unknown required amounts are null, not zero.
- Where compatible selected reports can be added, retain their signs: a synthetic
  `10000` and `-4000` produces `6000` as a selected-report subtotal, not a tax bill.
  Do not aggregate incompatible policy versions silently or apply a zero floor
  to each portfolio. Do not pretend summation resolves cross-portfolio tax-lot rules.
- `Draft` and `isTaxReady: false` remain mandatory even after the user confirms
  coverage. No PIT/levy amount, accepted loss deduction, official XML/PDF/form,
  signature, submission or silent legal-readiness transition.
- Provide save/list/get and full-precision JSON export in the next contract.
  Keep read/export deterministic from the snapshot; track changes to current
  source coverage separately. Use the linked annual contract for client paths.
- Backend owns arithmetic and access checks. Web foundation remains a separate
  pending stage; no frontend tax calculator or new infrastructure service is needed.

### Acceptance cases for that stage

- Two accounts and a non-owner admin cannot access each other's annual drafts;
  state changes require CSRF and an active account.
- Duplicate portfolio/report selections, wrong years, invalid hashes and known
  source overlap fail atomically. Archived owned reports remain usable.
- Missing external coverage or prior-loss information stays visibly unknown;
  confirmed zero remains zero. Claims do not reduce the displayed subtotal.
- Replacing a selected report or correcting source trades never mutates old annual
  or portfolio snapshots. New/changed coverage is surfaced separately.
- Mixed calculation versions are rejected or explicitly separated, never blended
  into a misleading total. Decimal serialization/storage introduces no extra rounding.
- Synthetic gain/loss arithmetic, negative totals, empty years, export integrity
  and source provenance are tested against disposable PostgreSQL.
- Use the [sample audit](../domain/spreadsheet-sample-audit.md) for reconciliation.
  Use the user's confirmed BXMT purchase-price correction (18.08 USD), retaining
  the discrepancy and correction provenance rather than copying the original C5.
  SCHD needs an independently sourced split effective event and complete batch
  reconstruction before an end-to-end split fixture is claimed.

## Verification of this documentation stage

This stage inspects official HTML guidance, current repository contracts and eight
read-only sample workbooks. It introduces no application code or migration.
Verification passed: 13 changed Markdown files use LF, all 72 checked local link
targets exist, and `git diff --check` passes in each of the three repositories.
The previous code stage's 241 passing tests are historical evidence, not a test
run for this documentation stage. No user database changes or deployment are needed.
