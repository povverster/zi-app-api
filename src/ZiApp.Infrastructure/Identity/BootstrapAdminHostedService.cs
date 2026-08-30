using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ZiApp.Application.Accounts;
using ZiApp.Domain.Accounts;
using ZiApp.Infrastructure.Persistence;

namespace ZiApp.Infrastructure.Identity;

public sealed class BootstrapAdminHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<BootstrapAdminOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        BootstrapAdminOptions settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        Validate(settings);

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        bool superAdminExists = await dbContext.UserAccounts
            .AnyAsync(account => account.Role == AccountRole.SuperAdmin, cancellationToken);

        if (superAdminExists)
        {
            return;
        }

        var provisioningService = scope.ServiceProvider
            .GetRequiredService<IAccountProvisioningService>();
        AccountProvisioningResult result = await provisioningService.ProvisionAsync(
            new ProvisionAccountCommand(
                settings.Email,
                settings.DisplayName,
                settings.Password,
                AccountRole.SuperAdmin,
                settings.PreferredLanguage),
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"The bootstrap super administrator could not be created: {string.Join(" ", result.Errors)}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static void Validate(BootstrapAdminOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Email)
            || string.IsNullOrWhiteSpace(settings.DisplayName)
            || string.IsNullOrWhiteSpace(settings.Password))
        {
            throw new InvalidOperationException(
                "BootstrapAdmin email, display name, and password are required when bootstrapping is enabled.");
        }
    }
}