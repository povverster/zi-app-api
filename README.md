# ZiApp API

ASP.NET Core backend for the Zorjd Investments application.

## Foundation

- .NET 10 and ASP.NET Core controllers
- PostgreSQL 18 through Entity Framework Core and Npgsql
- Swagger/OpenAPI in Development and Testing
- Repository-local EF Core migration tooling
- Unit tests plus PostgreSQL integration tests
- Multi-stage, non-root Docker image

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

## Tests

```powershell
dotnet test ZiApp.sln --configuration Release
```

Integration tests create a temporary PostgreSQL container and apply real migrations.

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
