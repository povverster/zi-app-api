using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ZiApp.Domain.Accounts;
using ZiApp.Domain.ExchangeRates;
using ZiApp.Domain.Instruments;
using ZiApp.Domain.Portfolios;
using ZiApp.Domain.TaxReports;
using ZiApp.Domain.Transactions;

namespace ZiApp.Infrastructure.Persistence.Configurations;

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("user_accounts", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_user_accounts_role",
                "role IN ('User', 'SuperAdmin')");
            tableBuilder.HasCheckConstraint(
                "ck_user_accounts_preferred_language",
                "preferred_language IN ('English', 'Ukrainian', 'Russian')");
        });

        builder.HasKey(account => account.Id)
            .HasName("pk_user_accounts");

        builder.Property(account => account.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(account => account.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();
        builder.Property(account => account.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(320)
            .IsRequired();
        builder.Property(account => account.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(account => account.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(account => account.PreferredLanguage)
            .HasColumnName("preferred_language")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(account => account.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(account => account.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(account => account.NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("ux_user_accounts_normalized_email");
    }
}

public sealed class PortfolioConfiguration : IEntityTypeConfiguration<Portfolio>
{
    public void Configure(EntityTypeBuilder<Portfolio> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("portfolios");
        builder.HasKey(portfolio => portfolio.Id)
            .HasName("pk_portfolios");

        builder.Property(portfolio => portfolio.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(portfolio => portfolio.OwnerAccountId)
            .HasColumnName("owner_account_id")
            .IsRequired();
        builder.Property(portfolio => portfolio.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(portfolio => portfolio.BaseCurrencyCode)
            .HasColumnName("base_currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();
        builder.Property(portfolio => portfolio.IsArchived)
            .HasColumnName("is_archived")
            .IsRequired();
        builder.Property(portfolio => portfolio.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne(portfolio => portfolio.OwnerAccount)
            .WithMany()
            .HasForeignKey(portfolio => portfolio.OwnerAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_portfolios_user_accounts_owner_account_id");

        builder.HasIndex(portfolio => new { portfolio.OwnerAccountId, portfolio.Name })
            .IsUnique()
            .HasDatabaseName("ux_portfolios_owner_account_id_name");
    }
}

public sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("instruments", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_instruments_type",
                "type IN ('Stock', 'Etf')");
        });

        builder.HasKey(instrument => instrument.Id)
            .HasName("pk_instruments");

        builder.Property(instrument => instrument.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(instrument => instrument.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(instrument => instrument.ExchangeCode)
            .HasColumnName("exchange_code")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(instrument => instrument.Name)
            .HasColumnName("name")
            .HasMaxLength(300)
            .IsRequired();
        builder.Property(instrument => instrument.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(instrument => instrument.CurrencyCode)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();
        builder.Property(instrument => instrument.Isin)
            .HasColumnName("isin")
            .HasMaxLength(12)
            .IsFixedLength();

        builder.HasIndex(instrument => new { instrument.Symbol, instrument.ExchangeCode })
            .IsUnique()
            .HasDatabaseName("ux_instruments_symbol_exchange_code");
        builder.HasIndex(instrument => instrument.Isin)
            .IsUnique()
            .HasFilter("isin IS NOT NULL")
            .HasDatabaseName("ux_instruments_isin");
    }
}

public sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("exchange_rates", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_exchange_rates_rate_to_uah", "rate_to_uah > 0");
        });

        builder.HasKey(rate => rate.Id)
            .HasName("pk_exchange_rates");

        builder.Property(rate => rate.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(rate => rate.CurrencyCode)
            .HasColumnName("currency_code")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();
        builder.Property(rate => rate.EffectiveDate)
            .HasColumnName("effective_date")
            .IsRequired();
        builder.Property(rate => rate.RateToUah)
            .HasColumnName("rate_to_uah")
            .HasPrecision(20, 10)
            .IsRequired();
        builder.Property(rate => rate.Source)
            .HasColumnName("source")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(rate => rate.RetrievedAtUtc)
            .HasColumnName("retrieved_at_utc")
            .IsRequired();

        builder.HasIndex(rate => new { rate.CurrencyCode, rate.EffectiveDate, rate.Source })
            .IsUnique()
            .HasDatabaseName("ux_exchange_rates_currency_date_source");
    }
}

public sealed class InvestmentTransactionConfiguration : IEntityTypeConfiguration<InvestmentTransaction>
{
    public void Configure(EntityTypeBuilder<InvestmentTransaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("investment_transactions", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_investment_transactions_side",
                "side IN ('Buy', 'Sell')");
            tableBuilder.HasCheckConstraint(
                "ck_investment_transactions_quantity",
                "quantity > 0");
            tableBuilder.HasCheckConstraint(
                "ck_investment_transactions_unit_price_usd",
                "unit_price_usd > 0");
            tableBuilder.HasCheckConstraint(
                "ck_investment_transactions_fee_usd",
                "fee_usd >= 0");
        });

        builder.HasKey(transaction => transaction.Id)
            .HasName("pk_investment_transactions");

        builder.Property(transaction => transaction.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(transaction => transaction.PortfolioId)
            .HasColumnName("portfolio_id")
            .IsRequired();
        builder.Property(transaction => transaction.InstrumentId)
            .HasColumnName("instrument_id")
            .IsRequired();
        builder.Property(transaction => transaction.ExchangeRateId)
            .HasColumnName("exchange_rate_id")
            .IsRequired();
        builder.Property(transaction => transaction.Side)
            .HasColumnName("side")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(transaction => transaction.ExecutedAtUtc)
            .HasColumnName("executed_at_utc")
            .IsRequired();
        builder.Property(transaction => transaction.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(28, 12)
            .IsRequired();
        builder.Property(transaction => transaction.UnitPriceUsd)
            .HasColumnName("unit_price_usd")
            .HasPrecision(28, 12)
            .IsRequired();
        builder.Property(transaction => transaction.FeeUsd)
            .HasColumnName("fee_usd")
            .HasPrecision(28, 12)
            .IsRequired();
        builder.Property(transaction => transaction.BrokerTransactionId)
            .HasColumnName("broker_transaction_id")
            .HasMaxLength(200);

        builder.HasOne(transaction => transaction.Portfolio)
            .WithMany()
            .HasForeignKey(transaction => transaction.PortfolioId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_investment_transactions_portfolios_portfolio_id");
        builder.HasOne(transaction => transaction.Instrument)
            .WithMany()
            .HasForeignKey(transaction => transaction.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_investment_transactions_instruments_instrument_id");
        builder.HasOne(transaction => transaction.ExchangeRate)
            .WithMany()
            .HasForeignKey(transaction => transaction.ExchangeRateId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_investment_transactions_exchange_rates_exchange_rate_id");

        builder.HasIndex(transaction => transaction.ExchangeRateId)
            .HasDatabaseName("ix_investment_transactions_exchange_rate_id");
        builder.HasIndex(transaction => transaction.InstrumentId)
            .HasDatabaseName("ix_investment_transactions_instrument_id");
        builder.HasIndex(transaction => new
        {
            transaction.PortfolioId,
            transaction.ExecutedAtUtc,
            transaction.Id,
        })
            .HasDatabaseName("ix_investment_transactions_fifo_order");
        builder.HasIndex(transaction => new { transaction.PortfolioId, transaction.BrokerTransactionId })
            .IsUnique()
            .HasFilter("broker_transaction_id IS NOT NULL")
            .HasDatabaseName("ux_investment_transactions_portfolio_broker_id");
    }
}

public sealed class StockSplitConfiguration : IEntityTypeConfiguration<StockSplit>
{
    public void Configure(EntityTypeBuilder<StockSplit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("stock_splits", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_stock_splits_numerator", "numerator > 0");
            tableBuilder.HasCheckConstraint("ck_stock_splits_denominator", "denominator > 0");
            tableBuilder.HasCheckConstraint("ck_stock_splits_changes_units", "numerator <> denominator");
        });

        builder.HasKey(split => split.Id)
            .HasName("pk_stock_splits");

        builder.Property(split => split.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(split => split.InstrumentId)
            .HasColumnName("instrument_id")
            .IsRequired();
        builder.Property(split => split.EffectiveAtUtc)
            .HasColumnName("effective_at_utc")
            .IsRequired();
        builder.Property(split => split.Numerator)
            .HasColumnName("numerator")
            .HasPrecision(28, 12)
            .IsRequired();
        builder.Property(split => split.Denominator)
            .HasColumnName("denominator")
            .HasPrecision(28, 12)
            .IsRequired();

        builder.HasOne(split => split.Instrument)
            .WithMany()
            .HasForeignKey(split => split.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_stock_splits_instruments_instrument_id");

        builder.HasIndex(split => new { split.InstrumentId, split.EffectiveAtUtc, split.Id })
            .HasDatabaseName("ix_stock_splits_instrument_effective_order");
    }
}

public sealed class TaxCalculationRunConfiguration : IEntityTypeConfiguration<TaxCalculationRun>
{
    public void Configure(EntityTypeBuilder<TaxCalculationRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tax_calculation_runs", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_tax_calculation_runs_tax_year",
                "tax_year BETWEEN 2000 AND 9999");
        });

        builder.HasKey(run => run.Id)
            .HasName("pk_tax_calculation_runs");

        builder.Property(run => run.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(run => run.PortfolioId)
            .HasColumnName("portfolio_id")
            .IsRequired();
        builder.Property(run => run.TaxYear)
            .HasColumnName("tax_year")
            .IsRequired();
        builder.Property(run => run.CalculationVersion)
            .HasColumnName("calculation_version")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(run => run.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasOne(run => run.Portfolio)
            .WithMany()
            .HasForeignKey(run => run.PortfolioId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tax_calculation_runs_portfolios_portfolio_id");

        builder.HasIndex(run => new { run.PortfolioId, run.TaxYear, run.CreatedAtUtc })
            .HasDatabaseName("ix_tax_calculation_runs_portfolio_year_created");
    }
}

public sealed class TaxLotMatchSnapshotConfiguration : IEntityTypeConfiguration<TaxLotMatchSnapshot>
{
    public void Configure(EntityTypeBuilder<TaxLotMatchSnapshot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tax_lot_match_snapshots", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_tax_lot_match_snapshots_quantity",
                "matched_quantity > 0");
        });

        builder.HasKey(match => match.Id)
            .HasName("pk_tax_lot_match_snapshots");

        builder.Property(match => match.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(match => match.TaxCalculationRunId)
            .HasColumnName("tax_calculation_run_id")
            .IsRequired();
        builder.Property(match => match.PurchaseTransactionId)
            .HasColumnName("purchase_transaction_id")
            .IsRequired();
        builder.Property(match => match.SaleTransactionId)
            .HasColumnName("sale_transaction_id")
            .IsRequired();
        builder.Property(match => match.MatchedQuantity)
            .HasColumnName("matched_quantity")
            .HasPrecision(28, 12)
            .IsRequired();

        ConfigureAmount(builder, match => match.PurchaseCostUsd, "purchase_cost_usd");
        ConfigureAmount(builder, match => match.PurchaseCostUah, "purchase_cost_uah");
        ConfigureAmount(builder, match => match.SaleProceedsUsd, "sale_proceeds_usd");
        ConfigureAmount(builder, match => match.SaleProceedsUah, "sale_proceeds_uah");
        ConfigureAmount(builder, match => match.PurchaseFeeUsd, "purchase_fee_usd");
        ConfigureAmount(builder, match => match.PurchaseFeeUah, "purchase_fee_uah");
        ConfigureAmount(builder, match => match.SaleFeeUsd, "sale_fee_usd");
        ConfigureAmount(builder, match => match.SaleFeeUah, "sale_fee_uah");
        ConfigureAmount(builder, match => match.GrossDifferenceUsd, "gross_difference_usd");
        ConfigureAmount(builder, match => match.GrossDifferenceUah, "gross_difference_uah");
        ConfigureAmount(builder, match => match.ExpensesUsd, "expenses_usd");
        ConfigureAmount(builder, match => match.ExpensesUah, "expenses_uah");
        ConfigureAmount(builder, match => match.ProfitUsd, "profit_usd");
        ConfigureAmount(builder, match => match.ProfitUah, "profit_uah");

        builder.HasOne(match => match.TaxCalculationRun)
            .WithMany()
            .HasForeignKey(match => match.TaxCalculationRunId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_tax_lot_matches_tax_calculation_runs_run_id");
        builder.HasOne(match => match.PurchaseTransaction)
            .WithMany()
            .HasForeignKey(match => match.PurchaseTransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tax_lot_matches_transactions_purchase_id");
        builder.HasOne(match => match.SaleTransaction)
            .WithMany()
            .HasForeignKey(match => match.SaleTransactionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tax_lot_matches_transactions_sale_id");

        builder.HasIndex(match => match.PurchaseTransactionId)
            .HasDatabaseName("ix_tax_lot_matches_purchase_transaction_id");
        builder.HasIndex(match => match.SaleTransactionId)
            .HasDatabaseName("ix_tax_lot_matches_sale_transaction_id");
        builder.HasIndex(match => new
        {
            match.TaxCalculationRunId,
            match.PurchaseTransactionId,
            match.SaleTransactionId,
        })
            .IsUnique()
            .HasDatabaseName("ux_tax_lot_matches_run_purchase_sale");
    }

    private static void ConfigureAmount(
        EntityTypeBuilder<TaxLotMatchSnapshot> builder,
        System.Linq.Expressions.Expression<Func<TaxLotMatchSnapshot, decimal>> property,
        string columnName)
    {
        builder.Property(property)
            .HasColumnName(columnName)
            .HasPrecision(28, 12)
            .IsRequired();
    }
}