# Additional spreadsheet samples

Read-only inspection on 2026-10-03 of the user-provided sibling directory
`zi-samples` (local path `C:\Users\povve\Projects\zorjd-investments\zi-samples`).
No workbook was edited, exported, imported into the app, or added to Git.
Keep personal source files out of application repositories.

Each workbook contains the sheet `2024 - 2025` and the A:AD purchase/sale layout
described in the [calculation specification](tax-calculation-specification.md).
These are manually prepared calculation samples, not proof of broker history,
actual NBU rates or legal tax treatment. The directory may be absent on another PC;
do not make CI depend on an absolute path or on these personal files.

## Inspected cases

The UAH results below are **observed workbook subtotals shown to six decimals**,
not filing rounding or tax payable. Row counts are expanded match/unit rows,
not counts of unique broker transactions.

| Workbook | Sale rows by year | 2025 UAH profit cell | Observed subtotal | Status |
| --- | --- | --- | ---: | --- |
| BND_US.xlsx | 2025: 2 | AC5 | -73.735112 | Row arithmetic reconciles |
| BNDX_US.xlsx | 2025: 3 | AC6 | -273.673635 | Row arithmetic reconciles |
| BXMT_US.xlsx | 2025: 3 | AC6 | -410.150137 | User confirmed purchase price 18.08; original C5 differs |
| O_US.xlsx | 2025: 3 | AC6 | -792.847964 | Row arithmetic reconciles |
| SCHD_US.xlsx | 2024: 2; 2025: 99 | AC104 | 7633.319681 | Split/year-boundary case; see below |
| SCHX_US.xlsx | 2025: 17 | AC20 | 671.794148 | Row arithmetic reconciles |
| SLV_US.xlsx | 2025: 4 | AC7 | -46.020423 | Row arithmetic reconciles |
| TLT_US.xlsx | 2025: 41 | AC44 | 343.950486 | Matches the existing documented TLT result |

### Useful new coverage

- Purchases from 2024 feed sales in 2025. Broker year selection must not discard
  earlier acquisitions, and timestamps have no explicit timezone offsets in these
  sheets. Do not invent offsets for an import.
- SCHD has split factor `3` in `M5:M55`, while other rows use `1`.
  `F5 = E5/M5` and `I5 = (H5/L5)/M5` demonstrate adjusted unit cost and fee.
  The sheet does not provide a dedicated split-event timestamp/provenance record.
  Verify that separately before deriving application split inputs.
- SCHD's `AC104 = SUM(AC5:AC103)` intentionally excludes rows 3-4, whose sales
  are in 2024 (`O3:O4`). This provides an annual-selection case, not justification
  for deleting earlier FIFO history. Purchase and sale batch identities need
  reconstruction before building full ledger fixtures.
- BNDX and other multi-unit sales exercise fee allocation with repeating fractions.
  Preserve decimal precision; do not copy binary floating-point noise into fixtures.
- The original IBIT fixture is still in the existing tests/specification, but
  `IBIT_US.xlsx` is not present in this added directory.

### BXMT discrepancy and user correction

On the purchase dated 2024-12-23 in row 5:

- `C5` (unit price) is `17.01` and `D5` is `1`.
- `E5` is a hardcoded `18.08`, not `C5*D5`.
- `F5 = E5/M5` and downstream cost/profit values follow the higher purchase cost.

The user confirmed on 2026-10-03 that the correct purchase price is **18.08 USD**.
Use that price when preparing a regression fixture and record this correction's
provenance. The observed subtotal already follows that cost; the original `C5`
does not. The earlier mention of `8.08` was immediately corrected by the user.
The source workbook has not been edited by this task, and a broker statement
was not independently inspected.

The alternatives differ by `1.07 USD`, or `44.807427 UAH` at `B5 = 41.8761`.
Blindly using the original `C5` would produce approximately `-365.342710` UAH
instead of `-410.150137`. Do not alter the calculator to accept inconsistent inputs.

## Inspection and limits

The bundled spreadsheet reader inspected sheet layout, inputs, formulas and
subtotal ranges. A separate 50-digit decimal calculation read stored XLSX numeric
values and recomputed the row-level two-date formula, selecting sales in 2025.
Seven workbooks reconciled to cached row profits and annual subtotals within
`0.00000001 UAH`; BXMT row 5 was the only mismatch in that check. This tolerance
only accommodates Excel floating-point representation, not a filing rule.
The spreadsheet-reader error scan found no standard formula-error cells.

This was not a full independent FIFO/batch reconstruction, live NBU validation,
Excel recalculation session, legal audit or execution of the application's tests.
Shared-formula/cached cells were not treated as proof of new native recalculation.
No fixtures were added to the automated suite in this documentation stage.

### Follow-up implementation, 2026-10-04

The [annual preparation stage](../reports/annual-preparation-drafts.md) added
numerical BND, BNDX and corrected-BXMT fixtures to `AnnualSummaryApiTests`.
Both report APIs reconcile their UAH subtotals and the combined signed
`-757.558884` subtotal. The BXMT correction remains explicit; the original
workbook has not been changed. Synthetic timestamp offsets encode fixture
ordering only, not real broker timezone evidence. This adds neither live NBU
verification nor legal validation. SCHD still needs independent split-event
provenance and complete batch reconstruction before an end-to-end claim.

SHA-256 fingerprints identify the inspected files, not current broker truth.
Recheck when files change. Excel `~$` lock files are not workbooks and are excluded.

| File | SHA-256 |
| --- | --- |
| BND_US.xlsx | `c4be6e66a4c22fe3cc5550a2acaf5581b48b1f40aef0147bc6bc70c2894f4dc1` |
| BNDX_US.xlsx | `9ee944e5359f1a0a33b38ee103c638c921d43b4aea1d0a9653b1d0f7d9c7944a` |
| BXMT_US.xlsx | `c30ff63bd38401345f8e5cd9edf8167bd62b97b092349abeeb07591b7cc6fbca` |
| O_US.xlsx | `18f95ddab7f346d397a7360336bca1867ff14661f910fa44c44ba0c541b2505d` |
| SCHD_US.xlsx | `600872dd370a021fac999da35a902bca5cbff97d0eadf78d577a9ed8d9f772fd` |
| SCHX_US.xlsx | `170f673c892b678860668a046fdc8b668d60ddd22fb49b44144f26c4a375bbfb` |
| SLV_US.xlsx | `85b2d7cbef21a200c42fc1f4caf724fb6b2b67158fc88ebba2b602ae0ca414d2` |
| TLT_US.xlsx | `ad5570da55b6a4f5ca4e294f29bdba7ab91708ced02c906cca2e95c2795c96cb` |
