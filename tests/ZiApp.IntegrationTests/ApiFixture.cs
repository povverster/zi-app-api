using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.PostgreSql;

using ZiApp.Application.Accounts;
using ZiApp.Domain.Accounts;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@ziapp.test";

    public const string AdminPassword = "Testing!Password123";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:18.6-alpine")
        .WithDatabase("zi_app_tests")
        .WithUsername("zi_app")
        .WithPassword("zi_app_tests")
        .Build();

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        var provisioningService = scope.ServiceProvider
            .GetRequiredService<IAccountProvisioningService>();
        AccountProvisioningResult result = await provisioningService.ProvisionAsync(
            new ProvisionAccountCommand(
                AdminEmail,
                "Integration Super Administrator",
                AdminPassword,
                AccountRole.SuperAdmin,
                SupportedLanguage.English));

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"The integration-test administrator could not be created: {string.Join(" ", result.Errors)}");
        }

        Client = CreateClient();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Client?.Dispose();
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = _database.GetConnectionString(),
            });
        });
    }
}