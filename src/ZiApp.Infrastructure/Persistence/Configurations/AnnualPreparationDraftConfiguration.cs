using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ZiApp.Domain.TaxReports;

namespace ZiApp.Infrastructure.Persistence.Configurations;

public sealed class AnnualPreparationDraftConfiguration : IEntityTypeConfiguration<AnnualPreparationDraft>
{
    public void Configure(EntityTypeBuilder<AnnualPreparationDraft> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("annual_preparation_drafts", table =>
        {
            table.HasCheckConstraint("ck_annual_preparation_year", "tax_year = 2025");
            table.HasCheckConstraint("ck_annual_preparation_hash", "snapshot_sha256 ~ '^[0-9A-F]{64}$'");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.OwnerAccountId).HasColumnName("owner_account_id");
        builder.Property(r => r.TaxYear).HasColumnName("tax_year");
        builder.Property(r => r.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(r => r.SchemaVersion).HasColumnName("schema_version").HasMaxLength(100).IsRequired();
        builder.Property(r => r.SnapshotSha256).HasColumnName("snapshot_sha256").HasMaxLength(64).IsRequired();
        builder.Property(r => r.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("text").IsRequired();
        builder.HasOne(r => r.OwnerAccount).WithMany().HasForeignKey(r => r.OwnerAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.OwnerAccountId, r.TaxYear, r.CreatedAtUtc });
    }
}
