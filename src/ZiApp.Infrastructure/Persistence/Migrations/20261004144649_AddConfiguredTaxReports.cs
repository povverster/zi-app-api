using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZiApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguredTaxReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tax_settings_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    investment_income_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    military_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    dividend_income_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tax_settings_revisions", x => x.id);
                    table.CheckConstraint("ck_tax_settings_rates", "investment_income_percent BETWEEN 0 AND 100 AND military_percent BETWEEN 0 AND 100 AND dividend_income_percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_tax_settings_year", "tax_year BETWEEN 2000 AND 9998");
                    table.ForeignKey(
                        name: "FK_tax_settings_revisions_user_accounts_owner_account_id",
                        column: x => x.owner_account_id,
                        principalTable: "user_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "configured_tax_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settings_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    schema_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    snapshot_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_configured_tax_reports", x => x.id);
                    table.CheckConstraint("ck_configured_tax_hash", "snapshot_sha256 ~ '^[0-9A-F]{64}$'");
                    table.CheckConstraint("ck_configured_tax_year", "tax_year BETWEEN 2000 AND 9998");
                    table.ForeignKey(
                        name: "FK_configured_tax_reports_tax_calculation_runs_source_report_id",
                        column: x => x.source_report_id,
                        principalTable: "tax_calculation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_configured_tax_reports_tax_settings_revisions_settings_id",
                        column: x => x.settings_id,
                        principalTable: "tax_settings_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_configured_tax_reports_settings_id",
                table: "configured_tax_reports",
                column: "settings_id");

            migrationBuilder.CreateIndex(
                name: "IX_configured_tax_reports_source_report_id_created_at_utc",
                table: "configured_tax_reports",
                columns: new[] { "source_report_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_tax_settings_revisions_owner_account_id_tax_year_created_at~",
                table: "tax_settings_revisions",
                columns: new[] { "owner_account_id", "tax_year", "created_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM configured_tax_reports)
                        OR EXISTS (SELECT 1 FROM tax_settings_revisions) THEN
                        RAISE EXCEPTION 'Downgrade would erase saved configured tax reports or settings. Use a forward migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "configured_tax_reports");

            migrationBuilder.DropTable(
                name: "tax_settings_revisions");
        }
    }
}
