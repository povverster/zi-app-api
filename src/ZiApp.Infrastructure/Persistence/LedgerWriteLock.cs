using Microsoft.EntityFrameworkCore;

namespace ZiApp.Infrastructure.Persistence;

// Shared trade lock allows unrelated portfolios to write concurrently. Rare global split
// changes take the exclusive side BEFORE portfolio locks, including the first-trade case.
internal static class LedgerWriteLock
{
    private const long Key = 0x5A494150504C4544;
    public static Task SharedAsync(ApplicationDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({Key})", cancellationToken);
    public static Task ExclusiveAsync(ApplicationDbContext db, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({Key})", cancellationToken);
}