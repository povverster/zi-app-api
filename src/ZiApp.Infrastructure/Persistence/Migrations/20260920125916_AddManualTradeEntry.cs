using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZiApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManualTradeEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_investment_transactions_portfolio_broker_id",
                table: "investment_transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "exchange_rate_id",
                table: "investment_transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "fifo_order_id",
                table: "investment_transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "is_superseded",
                table: "investment_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserve same-instant FIFO order for every pre-existing trade.
            migrationBuilder.Sql("UPDATE investment_transactions SET fifo_order_id = id");

            migrationBuilder.AddColumn<string>(
                name: "executed_at_original",
                table: "investment_transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "trade_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_trade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_trade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trade_corrections", x => x.id);
                    table.CheckConstraint("ck_trade_corrections_distinct_trades", "original_trade_id <> replacement_trade_id");
                    table.CheckConstraint("ck_trade_corrections_reason", "length(trim(reason)) > 0");
                    table.ForeignKey(
                        name: "fk_trade_corrections_actor",
                        column: x => x.actor_account_id,
                        principalTable: "user_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trade_corrections_original",
                        column: x => x.original_trade_id,
                        principalTable: "investment_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_trade_corrections_replacement",
                        column: x => x.replacement_trade_id,
                        principalTable: "investment_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_investment_transactions_portfolio_broker_id",
                table: "investment_transactions",
                columns: new[] { "portfolio_id", "broker_transaction_id" },
                unique: true,
                filter: "broker_transaction_id IS NOT NULL AND NOT is_superseded");

            migrationBuilder.CreateIndex(
                name: "ix_trade_corrections_actor",
                table: "trade_corrections",
                column: "actor_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_trade_corrections_original",
                table: "trade_corrections",
                column: "original_trade_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_trade_corrections_replacement",
                table: "trade_corrections",
                column: "replacement_trade_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never erase audit history or substitute a fabricated FX record during rollback.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM trade_corrections)
                       OR EXISTS (SELECT 1 FROM investment_transactions WHERE exchange_rate_id IS NULL OR is_superseded)
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade while pending-rate trades or correction history exist. Use a forward migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "trade_corrections");

            migrationBuilder.DropIndex(
                name: "ux_investment_transactions_portfolio_broker_id",
                table: "investment_transactions");

            migrationBuilder.DropColumn(
                name: "fifo_order_id",
                table: "investment_transactions");

            migrationBuilder.DropColumn(
                name: "executed_at_original",
                table: "investment_transactions");

            migrationBuilder.DropColumn(
                name: "is_superseded",
                table: "investment_transactions");

            migrationBuilder.AlterColumn<Guid>(
                name: "exchange_rate_id",
                table: "investment_transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_investment_transactions_portfolio_broker_id",
                table: "investment_transactions",
                columns: new[] { "portfolio_id", "broker_transaction_id" },
                unique: true,
                filter: "broker_transaction_id IS NOT NULL");
        }
    }
}