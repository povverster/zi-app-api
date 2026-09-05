# ZiApp API

ASP.NET Core backend for the Zorjd Investments application.

## Foundation

- .NET 10 and ASP.NET Core controllers
- PostgreSQL 18 through Entity Framework Core and Npgsql
- Swagger/OpenAPI in Development and Testing
- Repository-local EF Core migration tooling
- Unit tests plus PostgreSQL integration tests
- Multi-stage, non-root Docker image
- ASP.NET Core Identity with admin-controlled account provisioning

## Repository layout

```text
src/
  ZiApp.Api/             HTTP endpoints and application startup
  ZiApp.Application/     use cases and application boundaries
  ZiApp.Domain/          investment and tax domain model
  ZiApp.Infrastructure/  PostgreSQL and external integrations
tests/
  ZiApp.UnitTests/
  ZiApp.IntegrationTests/
```

The backend starts as a modular monolith. Domain and Application remain independent
of ASP.NET Core, PostgreSQL, and other infrastructure details.

## Local development

Prerequisites:

- a .NET SDK compatible with `global.json`
- Docker Desktop
- the sibling `zi-app-infra` repository

Start PostgreSQL from the infrastructure repository:

```powershell
Copy-Item .env.example .env
docker compose -f compose.dev.yml up -d postgres
```

Then, from this repository:

```powershell
dotnet tool restore
dotnet restore --locked-mode
dotnet ef database update --project src/ZiApp.Infrastructure --startup-project src/ZiApp.Api
dotnet run --project src/ZiApp.Api --urls http://localhost:5050
```

Useful endpoints:

- `GET /health/live` confirms that the API process is running
- `GET /health` confirms that PostgreSQL is reachable
- `GET /swagger` opens interactive API documentation

## Authentication

Accounts are created only by a super administrator; there is no public registration
endpoint. See [the authentication guide](docs/security/authentication.md) for the
first-admin setup, cookie and CSRF flow, and security defaults.

## Tests

```powershell
dotnet test ZiApp.sln --configuration Release
```

Integration tests create a temporary PostgreSQL container and apply real migrations.

## Apply pending migrations

Run this after pulling changes that include new migrations. API startup does not
apply migrations automatically.

Start PostgreSQL as described in [Local development](#local-development), then run
the following commands from the `zi-app-api` repository root:

```powershell
dotnet tool restore
dotnet restore --locked-mode
```

The migration tool defaults to the local database at `localhost:5432` with database
and username `zi_app` and password `zi_app_local_dev`. If your database settings
differ, set `ConnectionStrings__Database` in the same PowerShell session before
running the EF commands. Replace the placeholders with your database settings:

```powershell
$env:ConnectionStrings__Database = "Host=<host>;Port=<port>;Database=<database>;Username=<username>;Password=<password>"
```

Confirm the connection targets the intended database, then list migrations:

```powershell
dotnet ef migrations list `
  --project src/ZiApp.Infrastructure `
  --startup-project src/ZiApp.Api
```

Migrations marked `(Pending)` have not been applied to that database. Apply all
pending migrations in order:

```powershell
dotnet ef database update `
  --project src/ZiApp.Infrastructure `
  --startup-project src/ZiApp.Api
```

EF Core tracks applied migrations in `__EFMigrationsHistory` and skips them on
subsequent runs. If the database is already up to date, this command makes no
schema changes. Run `dotnet ef migrations list` again with the same project
options to verify that no migrations remain marked `(Pending)`.

## Add a migration

```powershell
dotnet ef migrations add MigrationName `
  --project src/ZiApp.Infrastructure `
  --startup-project src/ZiApp.Api `
  --output-dir Persistence/Migrations
```

## Build the API image

```powershell
docker build --tag zi-app-api:dev .
```
