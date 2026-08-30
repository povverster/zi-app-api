using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ZiApp.Application.Accounts;
using ZiApp.Domain.Accounts;
using ZiApp.Infrastructure.Identity;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.IntegrationTests;

public sealed class AuthenticationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task IdentityMigrationIsApplied()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IEnumerable<string> migrations = await dbContext.Database.GetAppliedMigrationsAsync();

        Assert.Contains(migrations, migration =>
            migration.EndsWith("_AddIdentityAuthentication", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProtectedEndpointReturnsApiUnauthorizedResponse()
    {
        using HttpClient client = CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task LoginRejectsIncorrectPassword()
    {
        using HttpClient client = CreateClient();
        string csrfToken = await GetCsrfTokenAsync(client);

        using HttpResponseMessage response = await PostWithCsrfAsync(
            client,
            "/api/auth/login",
            new LoginPayload(ApiFixture.AdminEmail, "Incorrect!Password123"),
            csrfToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task LoginRequiresAntiforgeryHeader()
    {
        using HttpClient client = CreateClient();
        await GetCsrfTokenAsync(client);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginPayload(ApiFixture.AdminEmail, ApiFixture.AdminPassword),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiFixture.AdminPassword, responseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuperAdminCanCreateUserAccount()
    {
        using HttpClient client = CreateClient();
        await LoginAsync(client, ApiFixture.AdminEmail, ApiFixture.AdminPassword);
        string csrfToken = await GetCsrfTokenAsync(client);
        string email = $"investor-{Guid.CreateVersion7():N}@example.test";
        const string password = "Investor!Password123";

        using HttpResponseMessage response = await PostWithCsrfAsync(
            client,
            "/api/admin/accounts",
            new CreateAccountPayload(
                email,
                "Integration Investor",
                password,
                AccountRole.User,
                SupportedLanguage.Ukrainian),
            csrfToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AccountPayload? payload = await response.Content.ReadFromJsonAsync<AccountPayload>(JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal(email, payload.Email);
        Assert.Equal(AccountRole.User, payload.Role);
        Assert.Equal(7, payload.Id.Version);
        Assert.Equal(SupportedLanguage.Ukrainian, payload.PreferredLanguage);
        Assert.DoesNotContain(password, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var scope = fixture.Services.CreateAsyncScope();
        string normalizedEmail = email.ToUpperInvariant();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ApplicationUser storedUser = await dbContext.Users
            .Include(user => user.Account)
            .SingleAsync(user => user.NormalizedEmail == normalizedEmail);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IList<string> roles = await userManager.GetRolesAsync(storedUser);

        Assert.Equal(storedUser.AccountId, storedUser.Account.Id);
        Assert.Equal("Integration Investor", storedUser.Account.DisplayName);
        Assert.Equal(7, storedUser.Id.Version);
        Assert.Contains(nameof(AccountRole.User), roles);
    }

    [Fact]
    public async Task RegularUserCannotCreateAccounts()
    {
        string email = $"restricted-{Guid.CreateVersion7():N}@example.test";
        const string password = "Restricted!Password123";

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var provisioningService = scope.ServiceProvider
                .GetRequiredService<IAccountProvisioningService>();
            AccountProvisioningResult result = await provisioningService.ProvisionAsync(
                new ProvisionAccountCommand(
                    email,
                    "Restricted Investor",
                    password,
                    AccountRole.User,
                    SupportedLanguage.English));

            Assert.True(result.Succeeded, string.Join(" ", result.Errors));
        }

        using HttpClient client = CreateClient();
        await LoginAsync(client, email, password);
        string csrfToken = await GetCsrfTokenAsync(client);

        using HttpResponseMessage response = await PostWithCsrfAsync(
            client,
            "/api/admin/accounts",
            new CreateAccountPayload(
                $"blocked-{Guid.CreateVersion7():N}@example.test",
                "Blocked Account",
                "Blocked!Password123",
                AccountRole.User,
                SupportedLanguage.English),
            csrfToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task PublicRegistrationEndpointDoesNotExist()
    {
        using HttpClient client = CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new LoginPayload("someone@example.test", "Unused!Password123"),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient()
    {
        return fixture.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        string csrfToken = await GetCsrfTokenAsync(client);
        using HttpResponseMessage response = await PostWithCsrfAsync(
            client,
            "/api/auth/login",
            new LoginPayload(email, password),
            csrfToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();
        CsrfPayload? payload = await response.Content.ReadFromJsonAsync<CsrfPayload>(JsonOptions);

        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));
        return payload.Token;
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync<TPayload>(
        HttpClient client,
        string uri,
        TPayload payload,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);

        return await client.SendAsync(request);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record LoginPayload(string Email, string Password);

    private sealed record CreateAccountPayload(
        string Email,
        string DisplayName,
        string Password,
        AccountRole Role,
        SupportedLanguage PreferredLanguage);

    private sealed record CsrfPayload(string Token);

    private sealed record AccountPayload(
        Guid Id,
        string Email,
        string DisplayName,
        AccountRole Role,
        SupportedLanguage PreferredLanguage,
        bool IsActive);
}