using Microsoft.EntityFrameworkCore;

using ZiApp.Domain.Accounts;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;

namespace ZiApp.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<Portfolio> Portfolios => Set<Portfolio>();

    public DbSet<Instrument> Instruments => Set<Instrument>();

    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    public DbSet<InvestmentTransaction> InvestmentTransactions => Set<InvestmentTransaction>();

    public DbSet<StockSplit> StockSplits => Set<StockSplit>();

    public DbSet<TaxCalculationRun> TaxCalculationRuns => Set<TaxCalculationRun>();

    public DbSet<TaxLotMatchSnapshot> TaxLotMatchSnapshots => Set<TaxLotMatchSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}