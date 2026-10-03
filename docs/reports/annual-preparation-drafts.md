# Annual preparation drafts

Implemented as a bounded backend stage on 2026-10-04. This prepares the
user-approved 2025 Ukrainian personal foreign-broker stock/ETF-sales scope; it is
**not an official tax return or validated tax base**. The [filing review](ua-2025-filing-readiness.md)
still has open legal, FX, fee, FIFO, loss, rounding and form-version gates.
Existing [one-portfolio reports](draft-tax-reports.md) remain independent and unchanged.

## HTTP contract

All routes require an active account and return private, no-store responses.
Super administrators cannot access another owner's drafts. POST requires the
existing cookie plus `X-CSRF-TOKEN` flow.

| Method | Route | Result |
| --- | --- | --- |
| POST | `/api/annual-summaries` | Save a new immutable draft; 201 with Location |
| GET | `/api/annual-summaries?page=1&pageSize=20` | Owner-only paginated metadata |
| GET | `/api/annual-summaries/{id}` | Saved document |
| GET | `/api/annual-summaries/{id}/export` | Exact saved JSON bytes as an attachment |
| GET | `/api/annual-summaries/{id}/current-status` | Separate current-source comparison |

There are no update/delete, CSV, official-form, submission or tax-payable endpoints.
Only year 2025 is supported. Multiple annual drafts for that year are allowed.
Sources are selected explicitly, never automatically switched to the latest report.

Example POST; replace example IDs with owned saved portfolio-report IDs:

```json
{
  "taxYear": 2025,
  "reports": [
    {
      "portfolioId": "00000000-0000-7000-8000-000000000001",
      "reportId": "00000000-0000-7000-8000-000000000002",
      "brokerAccountReference": "Broker A / account alias"
    }
  ],
  "externalCoverage": "Unknown",
  "externalInvestments": [],
  "priorLossCoverage": "Unknown",
  "priorLossClaims": [],
  "portfolioCoverageConfirmed": true,
  "noOverlapConfirmed": true
}
```

Both confirmations must explicitly be true. They acknowledge review of the
declared selection/gaps and overlap; they do **not** attest legal completeness,
turn unknown amounts into zero, or remove review issues. Broker-account references
are user-supplied aliases, not credentials or proof. Null records unknown broker
context and creates a review issue.

Coverage states are case-sensitive:

- `Unknown`: information is missing; the corresponding list must be empty.
  External subtotal is null. Prior-loss coverage stays explicitly unknown.
- `None`: user confirms no such activity/claims; the list must be empty.
  External subtotal is the decimal string `"0"`.
- `Provided`: at least one input is required and remains unverified.
  An explicitly supplied zero is accepted.

For `externalCoverage: "Provided"`, each external item has:

```json
{
  "reference": "unique-statement-reference",
  "description": "Outside-app stock sales",
  "coverage": "Broker B account alias, 2025 sales not in selected reports",
  "profitUah": "-123.450000000001",
  "evidenceReference": "Statement page 4"
}
```

`profitUah` is the user's declared annual investment result, not an approved tax
amount. The API does not derive it from evidence. Reference keys must be unique
after trimming and case-insensitive comparison.

For `priorLossCoverage: "Provided"`, each claim has:

```json
{
  "originYear": 2024,
  "amountUah": "1000",
  "previouslyUsedUah": "200",
  "evidenceReference": "2024 declaration and supporting calculation"
}
```

Origin year is 2000–2024. Amounts are nonnegative; previously used cannot exceed
the claim. These checks establish input consistency only. Claims are never
deducted, accepted, or combined into an eligible carry-forward balance.
Evidence/coverage references are plain text; the server never fetches URLs.
Clients must render them as text, not HTML or automatically trusted links.

## Saved results and precision

Each snapshot preserves its UUIDv7 ID, owner/creator, microsecond UTC creation
time, schema `ziapp-annual-preparation-v1`, submitted selection/coverage/claims/
confirmations, portfolio inventory, omitted portfolio IDs, complete source report
documents and their original snapshot hashes.

`selectedReportsSubtotal` contains the six signed USD/UAH result strings from
compatible reports. For example, `10000 + -4000 = 6000`, without per-portfolio
zero floors. Addition does not resolve cross-portfolio legal tax-lot scope.
`externalProfitSubtotalUah` stays separate and unverified, not added to selected
results or reduced by claims. There is no combined taxable-income/payable field.

An empty selection has a selected subtotal of zero: the sum of an empty set, not
evidence of no taxpayer income. Unknown external coverage remains null.
Every document is `status: "Draft"`, `isTaxReady: false`, even with all confirmations.

Source policy tuples (schema/calculation/year/rounding) must agree. Source strings
retain full .NET decimal precision. Annual addition uses exact integer coefficients
and rejects a final result not representable as decimal, including sums that would
silently lose low digits in ordinary decimal addition. External/claim inputs use
numeric(28,12)-sized invariant strings; negative external results are allowed.
No exponent/locale comma/JSON numeric amounts, floating-point recomputation or
extra filing rounding.

## Coverage, duplicates and integrity

- At most one report per portfolio. Sources must belong to this owner, match 2025
  and have complete, hash-verified snapshots. Archived portfolios are supported.
  Foreign or mismatched portfolio/report IDs return not found without metadata leaks.
- Repeated trade IDs or stable FIFO keys are rejected. Broker execution IDs are
  checked within the declared broker-account alias; aliases are trimmed and
  compared without case, while execution IDs retain case.
- Identical instrument/side/UTC time/quantity/price/fee across portfolios produce
  `PossibleDuplicateTrades`, not rejection: distinct executions can look identical.
- Omitted owned portfolios remain visible without assuming tax relevance.
  `BrokerAccountCoverageUnknown`, `ExternalCoverageUnknown`,
  `PriorLossCoverageUnknown`, `ExternalInputsUnverified` and
  `PriorLossClaimsNotApplied` identify specific review gaps.
- `LegalReviewPending` and `ExternalOverlapNotAutomaticallyVerifiable` always
  remain. Confirmation cannot establish that external duplicates are absent.
- SHA-256 detects inconsistent bytes; it is not a digital signature or protection
  against a privileged database administrator.

Creation reads sources/inventory in one PostgreSQL Repeatable Read transaction
and saves only after all checks pass. Validation failure leaves no annual draft.
GET/export reads only saved bytes; it never resolves rates or fetches NBU data.

Current-status uses another consistent snapshot. It compares the captured own
portfolio inventory (IDs/names/archive state) and each selected report's integrity,
calculation version and current ledger inputs. It returns `Current` or
`ReviewRequired`, with source `Current`, `InputsChanged`, `CurrentInputsInvalid`,
`SourceInvalid` or `SourceUnavailableOrChanged`. Unknown comparison is null.
`Current` means unchanged observed inputs, **not complete or filing-ready**.
It does not inspect evidence, infer external updates or auto-select newer report
IDs. Corrections, renames and new portfolios never rewrite old exports.

## Bounds and errors

POST body limit: 256 KiB. Maximums: 20 reports, 100 external entries, 50 claims,
1000 owned portfolios, 16 MiB aggregate source JSON/final annual JSON.
Page size is 1–100 with positive, bounded page offset.
Text limits: broker alias/reference 200, description/coverage 500, evidence 1000.

401 means unauthenticated/inactive; 404 means missing/not owned.
400 `InvalidInput` covers year, missing confirmations, duplicate selections,
coverage/list mismatch, decimal/text/claim bounds or pagination. CSRF/malformed
JSON can use the normal framework 400 response. Oversized HTTP bodies return 413.
409 problem codes: `InvalidSource`, `LegacySnapshotUnavailable`,
`IncompatiblePolicies`, `KnownOverlap`, `TooLarge`, `ArithmeticNotRepresentable`,
`InvalidSnapshot`. Never reconstruct legacy snapshots from current inputs.

## Migration and acceptance

Apply `20261003152645_AddAnnualPreparationDrafts` after `AddDraftTaxReports`.
It adds only `annual_preparation_drafts` and its owner FK/index/checks; it does not
alter trades, prior snapshots, calculated values or source precision.
Snapshot text preserves exact exported bytes. Downgrade works only while this
table is empty; it refuses to destroy saved annual drafts.

Confirm and back up the target, use a controlled migration window and follow
[README migration instructions](../../README.md#apply-pending-migrations).
Do not reset tables/volumes. API startup does not apply migrations. Backups/logging
must treat external inputs, evidence references and embedded reports as private.
No new services, secrets or external network dependencies are needed.

Manual acceptance after migration:

1. Sign in, refresh CSRF, save two owned 2025 portfolio reports with verified rates.
2. POST their IDs and reviewed confirmations. Inspect signed subtotal, source
   hashes/policies and unknown-versus-none coverage.
3. Download JSON, correct a source trade and save a new portfolio report.
   The original annual export stays byte-identical; current-status flags changed
   inputs. A fresh annual POST creates another ID.
4. Repeat from another user and a non-owner super admin: selection, detail/status/
   export are unavailable; lists expose only their own drafts.

## Samples and verification

`AnnualSummaryApiTests` transcribes BND/BNDX/BXMT numerical fixtures from the
read-only [sample audit](../domain/spreadsheet-sample-audit.md), groups repeated
unit rows into buys/sells and runs both report APIs. BXMT uses the confirmed
18.08 USD correction. Results -73.735112, -273.673635 and -410.150137 UAH sum to
-757.558884 UAH without a zero floor.

Fixtures use synthetic `Z` offsets solely to encode supplied chronological order
and unchanged calendar dates. They are not broker imports or evidence of real
timezones. Rates are fixed test provenance, not live NBU verification.
No personal workbook is copied into Git or required by CI. SCHD's end-to-end split
case awaits independent split-event provenance and batch reconstruction.

Run from the API root:

```powershell
dotnet tool restore
dotnet restore ZiApp.sln --locked-mode
dotnet build ZiApp.sln --configuration Release --no-restore
dotnet test ZiApp.sln --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/ZiApp.Infrastructure --startup-project src/ZiApp.Api
git diff --check
```

Verification on 2026-10-04: tooling/locked restore and Release build passed with
zero warnings/errors; all 294 tests passed (93 unit, 201 PostgreSQL integration,
none skipped). EF reports no pending model changes. Changed-file LF and whitespace
checks passed across all three repositories; all 80 checked local documentation
link targets exist. Tests cover ownership including non-owner admins, active accounts,
CSRF, invalid/legacy/corrupt sources, coverage, sign/precision/version/overlap
rules, unchanged snapshots after corrections, exact exports and safe migration
upgrade/downgrade. Only disposable test databases were migrated, not the user's DB.
