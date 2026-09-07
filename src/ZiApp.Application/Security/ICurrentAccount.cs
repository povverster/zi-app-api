namespace ZiApp.Application.Security;

public interface ICurrentAccount
{
    Task<Guid?> GetActiveAccountIdAsync(CancellationToken cancellationToken = default);
}