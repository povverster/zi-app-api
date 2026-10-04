using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ZiApp.Domain.TaxReports;

namespace ZiApp.Infrastructure.Persistence.Configurations;

public sealed class TaxSettingsRevisionConfiguration : IEntityTypeConfiguration<TaxSettingsRevision>
{
    public void Configure(EntityTypeBuilder<TaxSettingsRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("tax_settings_revisions", table =>
        {
            table.HasCheckConstraint("ck_tax_settings_year", "tax_year BETWEEN 2000 AND 9998");
            table.HasCheckConstraint("ck_tax_settings_rates", "investment_income_percent BETWEEN 0 AND 100 AND military_percent BETWEEN 0 AND 100 AND dividend_income_percent BETWEEN 0 AND 100");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.OwnerAccountId).HasColumnName("owner_account_id");
        builder.Property(r => r.TaxYear).HasColumnName("tax_year");
        builder.Property(r => r.InvestmentIncomePercent).HasColumnName("investment_income_percent").HasPrecision(7, 4);
        builder.Property(r => r.MilitaryPercent).HasColumnName("military_percent").HasPrecision(7, 4);
        builder.Property(r => r.DividendIncomePercent).HasColumnName("dividend_income_percent").HasPrecision(7, 4);
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasOne(r => r.OwnerAccount).WithMany().HasForeignKey(r => r.OwnerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.OwnerAccountId, r.TaxYear, r.CreatedAtUtc, r.Id });
    }
}

public sealed class ConfiguredTaxReportConfiguration : IEntityTypeConfiguration<ConfiguredTaxReport>
{
    public void Configure(EntityTypeBuilder<ConfiguredTaxReport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("configured_tax_reports", table =>
        {
            table.HasCheckConstraint("ck_configured_tax_year", "tax_year BETWEEN 2000 AND 9998");
            table.HasCheckConstraint("ck_configured_tax_hash", "snapshot_sha256 ~ '^[0-9A-F]{64}$'");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.SourceReportId).HasColumnName("source_report_id");
        builder.Property(r => r.SettingsId).HasColumnName("settings_id");
        builder.Property(r => r.TaxYear).HasColumnName("tax_year");
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(r => r.SchemaVersion).HasColumnName("schema_version").HasMaxLength(100).IsRequired();
        builder.Property(r => r.SnapshotSha256).HasColumnName("snapshot_sha256").HasMaxLength(64).IsRequired();
        builder.Property(r => r.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("text").IsRequired();
        builder.HasOne(r => r.SourceReport).WithMany().HasForeignKey(r => r.SourceReportId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Settings).WithMany().HasForeignKey(r => r.SettingsId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.SourceReportId, r.CreatedAtUtc });
    }
}
