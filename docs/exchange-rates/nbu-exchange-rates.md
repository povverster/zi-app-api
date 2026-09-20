# NBU USD/UAH rates and trade-date policy

Implemented on 2026-09-20. This stage retrieves official dated rates, caches their
provenance, and resolves a trade's pending FX link. It does not calculate holdings,
generate reports, or make a trade filing-ready.

## Agreed broker-date policy

The user confirmed that **all broker dates are already correct, including old
records**. Never shift the transaction date to UTC, Kyiv, the browser timezone,
or the server timezone when selecting its exchange rate.

- Where `ExecutedAtOriginal` exists, use its calendar date with its supplied offset
  unchanged. Policy: `broker-recorded-date-nbu-exact-v1`.
- Where an older trade has no original string, use the calendar date already
  stored in `ExecutedAtUtc`, without conversion. Policy:
  `broker-stored-date-nbu-exact-v1`. This is an explicit legacy rule, not an
  inference about the original broker timezone.
- A malformed or inconsistent non-null original timestamp is a conflict requiring
  an audited correction, not a reason to silently use a different date.
- Example: `2025-01-05T23:30:00-05:00` selects **2025-01-05**, even though its
  normalized instant is on January 6. An old stored `2025-01-05T23:30:00Z`
  without an original string also selects January 5.
- Keep normalized UTC for chronological FIFO ordering, but do not derive the
  NBU date from it when the original broker timestamp exists. Preserving an
  absolute instant is distinct from changing a broker's calendar date.

This is an agreed software rule, not a legal conclusion about Ukrainian filings.
The [calculation specification](../domain/tax-calculation-specification.md)
still requires report rounding and legal validation before filing-ready reports.

## Official source and exact-date behavior

The source is the National Bank of Ukraine. See its
[open-data page](https://bank.gov.ua/ua/open-data) and
[exchange-rate API instructions](https://bank.gov.ua/admin_uploads/article/Instr_API_KURS_VAL_data.pdf).
The implementation requests one date and USD only:

```text
https://bank.gov.ua/NBU_Exchange/exchange_site?start=20250105&end=20250105&valcode=usd&sort=exchangedate&order=asc&json
```

Validate a single USD record (`cc=USD`, `r030=840`, `units=1`) whose
`exchangedate` matches the requested date. Use `rate_per_unit`; require agreement
with `rate`. Preserve `calcdate` separately: it is not the transaction/rate
effective date.

Weekend/holiday policy is also **exact-date**. Ask NBU for that calendar date.
Accept the official returned rate only when its effective date matches; do not
substitute Friday, a preceding business day, another provider, zero, or one.
Live checks on 2026-09-20 found the same 42.0385 rate on January 3, 4, and 5,
2025, each with its own requested effective date. The January 5 response had
calculation date January 2. These are observed examples, not an assumed rule for
every holiday. An empty array means missing; keep the trade pending and allow
a later retry. Missing/error responses are not cached.

Dates must be from 1996-09-02 through the current date in Kyiv
([NBU historical range](https://monetary-policy-debates.bank.gov.ua/ua/markets/exchangerate-chart)).
Kyiv is used only to bound requests for future NBU dates, never to convert trades.
The API Dockerfile installs `tzdata` for this guard because
[minimal Alpine .NET images omit timezone data](https://github.com/dotnet/dotnet-docker/blob/main/samples/enable-globalization.md).

## HTTP contract

All four endpoints require a signed-in active account. The two POST endpoints
require `X-CSRF-TOKEN`; follow the [authentication guide](../security/authentication.md).
They have no request body and do not accept a chosen rate, date, or owner for a trade.

| Method | Path | Behavior |
| --- | --- | --- |
| GET | `/api/exchange-rates/usd/{date}` | Read cached official rate; no network/write; 404 if not cached |
| POST | `/api/exchange-rates/usd/{date}/fetch` | Return cache hit or fetch, validate, and store; does not attach to trades |
| GET | `/api/portfolios/{portfolioId}/trades/{tradeId}/exchange-rate` | Owner-scoped status and linked-rate provenance |
| POST | `/api/portfolios/{portfolioId}/trades/{tradeId}/exchange-rate/resolve` | Select broker date, fetch/cache, and attach once with resolution audit |

Successful calls return 200. Dates in URLs use `yyyy-MM-dd`. Official rates are
shared public data; trade details remain owner-only even for super administrators.
All responses are no-store. GET never silently downloads a rate.

Rate responses contain `id`, `currencyCode`, `effectiveDate`, `rateToUah`,
`source`, `retrievedAtUtc`, `calculationDate`, `sourceUrl`, `responseSha256`,
and `attributionUrl`. **`rateToUah` is an invariant decimal JSON string** (for
example `"42.0385"`), never a binary floating-point number. Retain it as a string
in the client. New rates use source `NBU-ExchangeSite-v1`; show NBU attribution
and link to the source when presenting these data.

Trade responses contain `tradeId`, `status`, `selectedDate`, `policyVersion`,
`resolvedAtUtc`, `resolvedByAccountId`, nested `rate`, and `isTaxReady`.
The normal trade contract also exposes the same rate status:

- `Pending`: no rate attached.
- `LinkedUnverified`: an older link without the new selection/provenance audit.
- `Resolved`: attached with recorded selection policy, date, actor, and time.

**`isTaxReady` remains false for every status.** No background scheduler, batch
backfill, or automatic resolution at trade creation is added. Resolve explicitly
per trade. Current nonsuperseded resolved trades can feed a later FIFO workflow.

## Immutability, concurrency, and correction

The first validated rate is retained under the unique currency/date/source key.
Persist the exact UTF-8 JSON text, SHA-256, request URL, retrieval timestamp, and
NBU calculation date. The digest detects accidental payload changes; it is not
an NBU digital signature. Rates use PostgreSQL `numeric(20,10)`; reject excess
precision/range instead of silently rounding.

A concurrent insertion with the same rate and calculation date reuses the stored
winner, including its original response and retrieval time. A different rate or
calculation date returns `RateConflict` without replacing anything. There is no
refresh/overwrite endpoint: handling later NBU revisions needs a separately
designed audited workflow. Existing source `NBU` rows are not relabeled or used
as verified cache entries.

Preflight owner/state validation happens before NBU is called. Network waits do
not hold a portfolio lock. Re-read and validate the trade inside the same portfolio
row-lock boundary used for manual writes before attaching the rate. This catches
concurrent corrections/archiving. A fetched public rate may remain cached even
if a subsequent trade-state conflict prevents attachment.

Repeated resolution of a current resolved trade returns the same assignment.
Archived portfolios and superseded trades are readable but cannot be resolved.
Existing legacy links are preserved and return `LegacyRateLocked`; use the
[correction workflow](../trading/manual-trade-entry.md#audited-corrections) if
an intentional change is required. Corrections retain the resolved original
and create a new pending replacement. No source quantities, timestamps, fees,
FIFO keys, or saved report snapshots are rewritten by resolution.

## Failures and bounded network access

Rate business problems include a stable `code`:

| HTTP | Code | Action |
| --- | --- | --- |
| 400 | `InvalidDate` | Correct the out-of-range date |
| 404 | `RateMissing` | NBU has no exact-date rate; trade stays pending |
| 409 | `Archived`, `Superseded` | Restore the portfolio or use the current replacement |
| 409 | `LegacyRateLocked`, `InvalidOriginalTimestamp` | Inspect the source record; use audited correction if needed |
| 409 | `RateConflict` | Inspect the existing cache/trade; never force overwrite |
| 502 | `InvalidResponse` | Unvalidated NBU data are not stored or attached |
| 503 | `UpstreamUnavailable` | Retry later |

Foreign/missing trade/cache reads return 404; inactive/anonymous sessions return
401. Framework binding/CSRF failures return 400 and need not include a business code.

Network requests use a fixed HTTPS NBU endpoint without redirects. No API key or
user-supplied destination is needed. Each attempt times out after five seconds;
at most three attempts retry transport errors, timeouts, HTTP 408/429/5xx, with
250/500 ms delays. Short Retry-After values are respected; waits over two seconds
return 503 for a later caller retry. Caller cancellation propagates.
Responses are capped at 32 KiB and shallow JSON; duplicate properties, wrong
dates/currency/units, multiple records, invalid decimals, or inconsistent rates
are rejected. A malformed successful response is not retried.

## Migration and local acceptance

Apply **`20260920191527_AddNbuExchangeRates`** after `AddManualTradeEntry`,
following [the migration procedure](../../README.md#apply-pending-migrations).
It adds nullable provenance/resolution columns and supporting constraints/FK.
It preserves existing trades, rates, IDs, and snapshots; no reset is needed.
Downgrade is blocked once rate-response provenance or resolution history exists.
Use a forward migration rather than deleting audit history. API startup does not
apply migrations.

After applying migrations to the intended local database:

1. Log in and obtain a fresh CSRF token.
2. Create a trade with a broker offset crossing UTC midnight. GET its rate status:
   Pending. POST resolve and verify the selected date matches the original broker
   calendar date, with an exact rate string and NBU provenance.
3. Repeat resolve and GET: the rate ID, actor, and time stay unchanged. Fetch/read
   that date directly and verify the cached rate ID is the same.
4. Correct the trade: the original keeps its history; the replacement starts
   pending and can be resolved separately.
5. Try another account, missing CSRF, an archived portfolio, and a superseded
   original. Confirm rejection without altering the original history.

Automated tests use fixed NBU HTTP responses and disposable PostgreSQL databases,
not the user's database or a live NBU dependency. Coverage includes date/offset/
legacy cases, exact-date weekends, payload validation, retries and cancellation,
ownership, CSRF, disabled accounts, cache/assignment races, corrections during
fetch, provenance retention, migration upgrade and unsafe-downgrade refusal.

Verification on 2026-09-20: tooling/locked restore succeeded; Release build passed
with zero warnings/errors; all 162 tests passed (41 unit, 121 integration, none
skipped). EF reported no pending model changes. Changed code was formatted with
LF endings. The user's development database was not migrated or reset.
The Docker image also built successfully after including the repository's existing
SDK/analyzer configuration and timezone data. A network-disabled, read-only
container check verified the Kyiv zone file and non-root user. Documentation
links, LF endings, and whitespace checks passed in all three repositories.
