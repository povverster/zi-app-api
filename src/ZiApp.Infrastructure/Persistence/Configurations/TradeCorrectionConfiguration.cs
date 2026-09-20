using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ZiApp.Domain.Accounts;
using ZiApp.Domain.Transactions;

namespace ZiApp.Infrastructure.Persistence.Configurations;

public sealed class TradeCorrectionConfiguration : IEntityTypeConfiguration<TradeCorrection>
{
    public void Configure(EntityTypeBuilder<TradeCorrection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("trade_corrections", table =>
        {
            table.HasCheckConstraint("ck_trade_corrections_distinct_trades", "original_trade_id <> replacement_trade_id");
            table.HasCheckConstraint("ck_trade_corrections_reason", "length(trim(reason)) > 0");
        });
        builder.HasKey(item => item.Id).HasName("pk_trade_corrections");
        builder.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(item => item.OriginalTradeId).HasColumnName("original_trade_id");
        builder.Property(item => item.ReplacementTradeId).HasColumnName("replacement_trade_id");
        builder.Property(item => item.ActorAccountId).HasColumnName("actor_account_id");
        builder.Property(item => item.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(item => item.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasOne<InvestmentTransaction>().WithMany().HasForeignKey(item => item.OriginalTradeId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_trade_corrections_original");
        builder.HasOne<InvestmentTransaction>().WithMany().HasForeignKey(item => item.ReplacementTradeId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_trade_corrections_replacement");
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(item => item.ActorAccountId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_trade_corrections_actor");
        builder.HasIndex(item => item.OriginalTradeId).IsUnique().HasDatabaseName("ux_trade_corrections_original");
        builder.HasIndex(item => item.ReplacementTradeId).IsUnique().HasDatabaseName("ux_trade_corrections_replacement");
        builder.HasIndex(item => item.ActorAccountId).HasDatabaseName("ix_trade_corrections_actor");
    }
}