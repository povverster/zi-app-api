using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZiApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSplitManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correction_reason",
                table: "stock_splits",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "effective_at_original",
                table: "stock_splits",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fifo_order_id",
                table: "stock_splits",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "is_superseded",
                table: "stock_splits",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_split_id",
                table: "stock_splits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recorded_at_utc",
                table: "stock_splits",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recorded_by_account_id",
                table: "stock_splits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_reference",
                table: "stock_splits",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            // Preserve the stable ordering of existing split events without inventing provenance.
            migrationBuilder.Sql("UPDATE stock_splits SET fifo_order_id = id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_splits_recorded_by",
                table: "stock_splits",
                column: "recorded_by_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_splits_active_order",
                table: "stock_splits",
                columns: new[] { "instrument_id", "fifo_order_id" },
                unique: true,
                filter: "NOT is_superseded");

            migrationBuilder.CreateIndex(
                name: "ux_stock_splits_previous",
                table: "stock_splits",
                column: "previous_split_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_splits_correction",
                table: "stock_splits",
                sql: "(previous_split_id IS NULL AND correction_reason IS NULL) OR (previous_split_id IS NOT NULL AND previous_split_id <> id AND correction_reason IS NOT NULL AND recorded_by_account_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_splits_provenance",
                table: "stock_splits",
                sql: "(recorded_by_account_id IS NULL AND recorded_at_utc IS NULL AND effective_at_original IS NULL AND source_reference IS NULL) OR (recorded_by_account_id IS NOT NULL AND recorded_at_utc IS NOT NULL AND effective_at_original IS NOT NULL AND source_reference IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_stock_splits_previous",
                table: "stock_splits",
                column: "previous_split_id",
                principalTable: "stock_splits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_stock_splits_recorded_by",
                table: "stock_splits",
                column: "recorded_by_account_id",
                principalTable: "user_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM stock_splits WHERE recorded_at_utc IS NOT NULL OR is_superseded OR previous_split_id IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade while split provenance or correction history exists. Use a forward migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_stock_splits_previous",
                table: "stock_splits");

            migrationBuilder.DropForeignKey(
                name: "fk_stock_splits_recorded_by",
                table: "stock_splits");

            migrationBuilder.DropIndex(
                name: "ix_stock_splits_recorded_by",
                table: "stock_splits");

            migrationBuilder.DropIndex(
                name: "ux_stock_splits_active_order",
                table: "stock_splits");

            migrationBuilder.DropIndex(
                name: "ux_stock_splits_previous",
                table: "stock_splits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_splits_correction",
                table: "stock_splits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_splits_provenance",
                table: "stock_splits");

            migrationBuilder.DropColumn(
                name: "correction_reason",
                table: "stock_splits");

            migrationBuilder.DropColumn(
                name: "effective_at_original",
                table: "stock_splits");

            migrationBuilder.DropColumn(
                name: "fifo_order_id",
                table: "stock_splits");

            migrationBuilder.DropColumn(
                name: "is_superseded",
                table: "stock_splits");

            migrationBuilder.DropColumn(
                name: "previous_split_id",
                table: "stock_splits");

            migrationBuilder.DropColumn(
                name: "recorded_at_utc",
                table: "stock_splits");

            migrationBuilder.DropColumn(
                name: "recorded_by_account_id",
                table: "stock_splits");

            migrationBuilder.DropColumn(
                name: "source_reference",
                table: "stock_splits");
        }
    }
}