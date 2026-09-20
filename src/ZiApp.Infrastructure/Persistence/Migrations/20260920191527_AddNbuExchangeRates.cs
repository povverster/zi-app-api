using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZiApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNbuExchangeRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "exchange_rate_policy",
                table: "investment_transactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "exchange_rate_resolved_at_utc",
                table: "investment_transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "exchange_rate_resolved_by_account_id",
                table: "investment_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "exchange_rate_selected_date",
                table: "investment_transactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "calculation_date",
                table: "exchange_rates",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "raw_response_json",
                table: "exchange_rates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "response_sha256",
                table: "exchange_rates",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_url",
                table: "exchange_rates",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_investment_transactions_rate_resolved_by",
                table: "investment_transactions",
                column: "exchange_rate_resolved_by_account_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_investment_transactions_rate_resolution",
                table: "investment_transactions",
                sql: "(exchange_rate_policy IS NULL AND exchange_rate_selected_date IS NULL AND exchange_rate_resolved_at_utc IS NULL AND exchange_rate_resolved_by_account_id IS NULL) OR (exchange_rate_id IS NOT NULL AND exchange_rate_policy IS NOT NULL AND exchange_rate_selected_date IS NOT NULL AND exchange_rate_resolved_at_utc IS NOT NULL AND exchange_rate_resolved_by_account_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exchange_rates_nbu_provenance",
                table: "exchange_rates",
                sql: "source <> 'NBU-ExchangeSite-v1' OR (calculation_date IS NOT NULL AND source_url IS NOT NULL AND response_sha256 IS NOT NULL AND length(response_sha256) = 64 AND raw_response_json IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_investment_transactions_rate_resolved_by",
                table: "investment_transactions",
                column: "exchange_rate_resolved_by_account_id",
                principalTable: "user_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM investment_transactions WHERE exchange_rate_policy IS NOT NULL)
                       OR EXISTS (SELECT 1 FROM exchange_rates WHERE raw_response_json IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade while NBU provenance or rate resolution history exists. Use a forward migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_investment_transactions_rate_resolved_by",
                table: "investment_transactions");

            migrationBuilder.DropIndex(
                name: "ix_investment_transactions_rate_resolved_by",
                table: "investment_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_investment_transactions_rate_resolution",
                table: "investment_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_exchange_rates_nbu_provenance",
                table: "exchange_rates");

            migrationBuilder.DropColumn(
                name: "exchange_rate_policy",
                table: "investment_transactions");

            migrationBuilder.DropColumn(
                name: "exchange_rate_resolved_at_utc",
                table: "investment_transactions");

            migrationBuilder.DropColumn(
                name: "exchange_rate_resolved_by_account_id",
                table: "investment_transactions");

            migrationBuilder.DropColumn(
                name: "exchange_rate_selected_date",
                table: "investment_transactions");

            migrationBuilder.DropColumn(
                name: "calculation_date",
                table: "exchange_rates");

            migrationBuilder.DropColumn(
                name: "raw_response_json",
                table: "exchange_rates");

            migrationBuilder.DropColumn(
                name: "response_sha256",
                table: "exchange_rates");

            migrationBuilder.DropColumn(
                name: "source_url",
                table: "exchange_rates");
        }
    }
}