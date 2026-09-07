# Portfolio management API

Implemented on 2026-09-07. This stage adds account-scoped portfolio management;
instrument/trade entry and tax-report generation remain later stages.

## Access and ownership

Every endpoint requires an authenticated, active domain account. The API resolves
the domain account from the Identity user in the session on each request.
Request payloads cannot choose the owner, identifier, base currency, archive
state, or creation time. New portfolios receive a UUIDv7 and base currency USD.

Users, including super administrators, see only their own portfolios. Access to
another account's portfolio returns the same `404` as a nonexistent identifier.
Deactivated accounts cannot use existing sessions to access these endpoints.
Responses are marked `Cache-Control: no-store`.

All mutations require the `X-CSRF-TOKEN` header. Follow the
[authentication flow](../security/authentication.md), including obtaining a fresh
token after login.

## Endpoints

| Method | Path | Successful behavior |
| --- | --- | --- |
| GET | `/api/portfolios` | `200`: paginated list of the current account's portfolios |
| GET | `/api/portfolios/{id}` | `200`: details, including an archived portfolio |
| POST | `/api/portfolios` | `201`: create; `Location` points to the details endpoint |
| PUT | `/api/portfolios/{id}` | `200`: rename only |
| POST | `/api/portfolios/{id}/archive` | `200`: archive; repeated calls are safe |
| POST | `/api/portfolios/{id}/restore` | `200`: restore; repeated calls are safe |

Create and rename accept:

```json
{
  "name": "Long-term investments"
}
```

Names are required, non-whitespace, and at most 200 characters in the submitted
value. Leading/trailing whitespace is trimmed before storage. Names are unique
per owner, using the existing database's case-sensitive comparison: `Main` and
`main` are different names. Different owners can reuse the same name. Renaming
a portfolio to its own name is allowed. Archived portfolios still reserve their
names. Duplicate creation/rename returns `409`, including concurrent attempts,
without exposing database errors.

A portfolio response contains `id`, `name`, `baseCurrencyCode`, `isArchived`,
and `createdAtUtc`. Creation returns the persisted timestamp so later reads
have the same value. Renaming and archive/restore preserve the original ID,
owner, currency, and creation time.

The list accepts `page` (default 1) and `pageSize` (default 20, maximum 100).
Page numbers must be positive, and the calculated offset must fit a signed
32-bit integer. Invalid values return `400`. Results are ordered by creation
timestamp, then ID. For example:

```text
GET /api/portfolios?page=1&pageSize=20&includeArchived=true
```

The response has `items`, `page`, `pageSize`, and `totalCount`.
Archived portfolios are excluded by default; `includeArchived=true` returns
both active and archived portfolios. The count applies only to the signed-in
account and selected archive filter. An empty or out-of-range page has an empty
`items` array.

Missing/invalid input or CSRF tokens return `400`. Missing authentication or an
inactive account returns `401`. Missing/other-owner portfolios return `404`.
Name conflicts return `409`. The interactive contract is in Swagger.

## Archive policy and stored data

Hard deletion is not exposed. `DELETE /api/portfolios/{id}` returns `405`;
use archive instead. Both empty and populated portfolios can be archived or
restored. Renaming an archived portfolio is allowed.

Archiving changes only the portfolio's archive flag. It does not remove or
rewrite trades, exchange rates, tax calculation runs, or FIFO match snapshots.
Historical data stays available to future authorized reporting workflows.
When trade-entry workflows are added, they must reject new trades into archived
portfolios until the owner restores them.

No new migration is required: `name`, `is_archived`, and the owner/name unique
index already exist in `InitialInvestmentLedger`. A fresh local database still
needs the existing three migrations, including `AddIdentityAuthentication`.
See the [database setup instructions](../../README.md).

## Implementation

- `ZiApp.Domain/Portfolios/Portfolio.cs`: rename/archive/restore behavior.
- `ZiApp.Application/Portfolios`: use cases, DTOs, and the repository boundary.
- `ZiApp.Application/Security/ICurrentAccount.cs`: active-account abstraction.
- `ZiApp.Infrastructure/Persistence/PortfolioRepository.cs`: owner-scoped queries
  and database-enforced name-conflict handling.
- `ZiApp.Api/Accounts/CurrentAccount.cs`: authenticated Identity-to-account lookup.
- `ZiApp.Api/Controllers/PortfoliosController.cs`: HTTP and CSRF contract.

## Manual acceptance check

Start PostgreSQL, apply the existing migrations, and run the API using the
[README](../../README.md). Bootstrap the first administrator if needed using the
[authentication guide](../security/authentication.md). Use the same browser or
HTTP client cookie session throughout; send its fresh CSRF header on mutations.

1. Without signing in, request `GET /api/portfolios`; expect `401`.
2. Sign in, get a fresh CSRF token, and create a portfolio named `Long term`.
   Expect `201`, a UUIDv7 ID, USD currency, and a working `Location`.
3. List/get the portfolio, then rename it. Verify the renamed value persists.
   Attempt the same name on another portfolio; expect `409`.
4. Archive it twice. Confirm it disappears from the default list, remains
   retrievable by ID and with `includeArchived=true`, and retains its data.
5. Restore it twice and verify it appears in the default list again.
6. Repeat a mutation with the CSRF header missing; expect `400` and no change.
7. Sign in as another account, including a super administrator. Confirm the first
   account's portfolios are absent from the list, and its IDs return `404` for
   reads, renames, archive, and restore.
8. Attempt `DELETE`; expect `405` and preserved data.

## Verification recorded for this stage

On 2026-09-07:

- Repository tools and locked packages restored successfully.
- Release build passed with zero warnings and errors.
- All 51 tests passed: 12 unit tests and 39 PostgreSQL integration tests.
  Portfolio coverage includes access isolation, spoofed fields, disabled accounts,
  CSRF, input validation, pagination, duplicate/concurrent names, archive/restore,
  saved trades/report snapshots, and Swagger.
- EF Core reported no pending model changes.
- A live HTTP smoke check using the installed .NET host and a disposable PostgreSQL
  database passed login/CSRF, creation/read/rename, duplicate rejection, archive/
  list/restore, unavailable hard deletion, readiness, and Swagger checks.
  The temporary process/database were cleaned up; the user's database was not changed.
