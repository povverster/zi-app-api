using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class FoundationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task LivenessEndpointIsHealthy()
    {
        var response = await fixture.Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadinessEndpointCanReachPostgreSql()
    {
        var response = await fixture.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InitialFoundationMigrationIsApplied()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var migrations = await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains(migrations, migration =>
            migration.EndsWith("_InitialFoundation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SwaggerDocumentIsAvailable()
    {
        var response = await fixture.Client.GetAsync("/swagger/v1/swagger.json");
        var document = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", document);
        Assert.Contains("ZiApp API", document);
    }
}