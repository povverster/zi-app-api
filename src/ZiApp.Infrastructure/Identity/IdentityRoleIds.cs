namespace ZiApp.Infrastructure.Identity;

internal static class IdentityRoleIds
{
    public static readonly Guid User = new("b2d7b0ee-a97b-4a54-ad7c-f6c74816b178");

    public static readonly Guid SuperAdmin = new("cbcc71a8-3b4c-4f1d-aaad-18687166d74a");

    public const string UserConcurrencyStamp = "f519c3dd-b53f-4aed-933e-65b631f77304";

    public const string SuperAdminConcurrencyStamp = "935c9d7d-484c-477d-b7c9-ce6835d454ba";
}