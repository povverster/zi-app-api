using ZiApp.Domain.Accounts;

namespace ZiApp.Infrastructure.Identity;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public bool Enabled { get; set; }

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Super Administrator";

    public string Password { get; set; } = string.Empty;

    public SupportedLanguage PreferredLanguage { get; set; } = SupportedLanguage.English;
}