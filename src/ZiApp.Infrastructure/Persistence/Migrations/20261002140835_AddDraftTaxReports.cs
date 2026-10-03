using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZiApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDraftTaxReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "sale_proceeds_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "sale_proceeds_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "sale_fee_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "sale_fee_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "purchase_fee_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "purchase_fee_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "purchase_cost_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "purchase_cost_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "profit_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "profit_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "matched_quantity",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "gross_difference_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "gross_difference_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "expenses_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AlterColumn<decimal>(
                name: "expenses_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(28,12)",
                oldPrecision: 28,
                oldScale: 12);

            migrationBuilder.AddColumn<string>(
                name: "input_sha256",
                table: "tax_calculation_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "report_schema_version",
                table: "tax_calculation_runs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_json",
                table: "tax_calculation_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_sha256",
                table: "tax_calculation_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_tax_calculation_runs_snapshot",
                table: "tax_calculation_runs",
                sql: "(report_schema_version IS NULL AND input_sha256 IS NULL AND snapshot_sha256 IS NULL AND snapshot_json IS NULL) OR (report_schema_version IS NOT NULL AND input_sha256 IS NOT NULL AND snapshot_sha256 IS NOT NULL AND snapshot_json IS NOT NULL AND input_sha256 ~ '^[0-9A-F]{64}$' AND snapshot_sha256 ~ '^[0-9A-F]{64}$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM tax_calculation_runs WHERE snapshot_json IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade while saved draft report snapshots exist. Use a forward migration.';
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM tax_lot_match_snapshots s
                        CROSS JOIN LATERAL unnest(ARRAY[
                            s.matched_quantity, s.purchase_cost_usd, s.purchase_cost_uah,
                            s.sale_proceeds_usd, s.sale_proceeds_uah, s.purchase_fee_usd, s.purchase_fee_uah,
                            s.sale_fee_usd, s.sale_fee_uah, s.gross_difference_usd, s.gross_difference_uah,
                            s.expenses_usd, s.expenses_uah, s.profit_usd, s.profit_uah]) AS amount(value)
                        WHERE value <> round(value, 12) OR abs(value) >= 10000000000000000)
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade without losing saved calculation precision. Use a forward migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "ck_tax_calculation_runs_snapshot",
                table: "tax_calculation_runs");

            migrationBuilder.DropColumn(
                name: "input_sha256",
                table: "tax_calculation_runs");

            migrationBuilder.DropColumn(
                name: "report_schema_version",
                table: "tax_calculation_runs");

            migrationBuilder.DropColumn(
                name: "snapshot_json",
                table: "tax_calculation_runs");

            migrationBuilder.DropColumn(
                name: "snapshot_sha256",
                table: "tax_calculation_runs");

            migrationBuilder.AlterColumn<decimal>(
                name: "sale_proceeds_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "sale_proceeds_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "sale_fee_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "sale_fee_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "purchase_fee_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "purchase_fee_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "purchase_cost_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "purchase_cost_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "profit_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "profit_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "matched_quantity",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "gross_difference_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "gross_difference_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "expenses_usd",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "expenses_uah",
                table: "tax_lot_match_snapshots",
                type: "numeric(28,12)",
                precision: 28,
                scale: 12,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");
        }
    }
}
