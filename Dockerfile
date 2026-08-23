FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source

COPY Directory.Build.props NuGet.config ./
COPY src/ZiApp.Api/ZiApp.Api.csproj src/ZiApp.Api/
COPY src/ZiApp.Application/ZiApp.Application.csproj src/ZiApp.Application/
COPY src/ZiApp.Domain/ZiApp.Domain.csproj src/ZiApp.Domain/
COPY src/ZiApp.Infrastructure/ZiApp.Infrastructure.csproj src/ZiApp.Infrastructure/
COPY src/ZiApp.Api/packages.lock.json src/ZiApp.Api/
COPY src/ZiApp.Application/packages.lock.json src/ZiApp.Application/
COPY src/ZiApp.Domain/packages.lock.json src/ZiApp.Domain/
COPY src/ZiApp.Infrastructure/packages.lock.json src/ZiApp.Infrastructure/

RUN dotnet restore src/ZiApp.Api/ZiApp.Api.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/ZiApp.Api/ZiApp.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID

ENTRYPOINT ["dotnet", "ZiApp.Api.dll"]
