using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZiApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInvestmentLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exchange_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    rate_to_uah = table.Column<decimal>(type: "numeric(20,10)", precision: 20, scale: 10, nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    retrieved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exchange_rates", x => x.id);
                    table.CheckConstraint("ck_exchange_rates_rate_to_uah", "rate_to_uah > 0");
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    exchange_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    isin = table.Column<string>(type: "character(12)", fixedLength: true, maxLength: 12, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.id);
                    table.CheckConstraint("ck_instruments_type", "type IN ('Stock', 'Etf')");
                });

            migrationBuilder.CreateTable(
                name: "user_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    preferred_language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_accounts", x => x.id);
                    table.CheckConstraint("ck_user_accounts_preferred_language", "preferred_language IN ('English', 'Ukrainian', 'Russian')");
                    table.CheckConstraint("ck_user_accounts_role", "role IN ('User', 'SuperAdmin')");
                });

            migrationBuilder.CreateTable(
                name: "stock_splits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    numerator = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    denominator = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_splits", x => x.id);
                    table.CheckConstraint("ck_stock_splits_changes_units", "numerator <> denominator");
                    table.CheckConstraint("ck_stock_splits_denominator", "denominator > 0");
                    table.CheckConstraint("ck_stock_splits_numerator", "numerator > 0");
                    table.ForeignKey(
                        name: "fk_stock_splits_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "portfolios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    base_currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_portfolios", x => x.id);
                    table.ForeignKey(
                        name: "fk_portfolios_user_accounts_owner_account_id",
                        column: x => x.owner_account_id,
                        principalTable: "user_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "investment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exchange_rate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    executed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    unit_price_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    fee_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    broker_transaction_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_investment_transactions", x => x.id);
                    table.CheckConstraint("ck_investment_transactions_fee_usd", "fee_usd >= 0");
                    table.CheckConstraint("ck_investment_transactions_quantity", "quantity > 0");
                    table.CheckConstraint("ck_investment_transactions_side", "side IN ('Buy', 'Sell')");
                    table.CheckConstraint("ck_investment_transactions_unit_price_usd", "unit_price_usd > 0");
                    table.ForeignKey(
                        name: "fk_investment_transactions_exchange_rates_exchange_rate_id",
                        column: x => x.exchange_rate_id,
                        principalTable: "exchange_rates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_investment_transactions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_investment_transactions_portfolios_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_calculation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    portfolio_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    calculation_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_calculation_runs", x => x.id);
                    table.CheckConstraint("ck_tax_calculation_runs_tax_year", "tax_year BETWEEN 2000 AND 9999");
                    table.ForeignKey(
                        name: "fk_tax_calculation_runs_portfolios_portfolio_id",
                        column: x => x.portfolio_id,
                        principalTable: "portfolios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tax_lot_match_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_calculation_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    matched_quantity = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    purchase_cost_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    purchase_cost_uah = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    sale_proceeds_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    sale_proceeds_uah = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    purchase_fee_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    purchase_fee_uah = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    sale_fee_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    sale_fee_uah = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    gross_difference_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    gross_difference_uah = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    expenses_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    expenses_uah = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    profit_usd = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false),
                    profit_uah = table.Column<decimal>(type: "numeric(28,12)", precision: 28, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tax_lot_match_snapshots", x => x.id);
                    table.CheckConstraint("ck_tax_lot_match_snapshots_quantity", "matched_quantity > 0");
                    table.ForeignKey(
                        name: "fk_tax_lot_matches_tax_calculation_runs_run_id",
                        column: x => x.tax_calculation_run_id,
                        principalTable: "tax_calculation_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tax_lot_matches_transactions_purchase_id",
                        column: x => x.purchase_transaction_id,
                        principalTable: "investment_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tax_lot_matches_transactions_sale_id",
                        column: x => x.sale_transaction_id,
                        principalTable: "investment_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_exchange_rates_currency_date_source",
                table: "exchange_rates",
                columns: new[] { "currency_code", "effective_date", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_instruments_isin",
                table: "instruments",
                column: "isin",
                unique: true,
                filter: "isin IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_instruments_symbol_exchange_code",
                table: "instruments",
                columns: new[] { "symbol", "exchange_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_investment_transactions_exchange_rate_id",
                table: "investment_transactions",
                column: "exchange_rate_id");

            migrationBuilder.CreateIndex(
                name: "ix_investment_transactions_fifo_order",
                table: "investment_transactions",
                columns: new[] { "portfolio_id", "executed_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_investment_transactions_instrument_id",
                table: "investment_transactions",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "ux_investment_transactions_portfolio_broker_id",
                table: "investment_transactions",
                columns: new[] { "portfolio_id", "broker_transaction_id" },
                unique: true,
                filter: "broker_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_portfolios_owner_account_id_name",
                table: "portfolios",
                columns: new[] { "owner_account_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_splits_instrument_effective_order",
                table: "stock_splits",
                columns: new[] { "instrument_id", "effective_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_calculation_runs_portfolio_year_created",
                table: "tax_calculation_runs",
                columns: new[] { "portfolio_id", "tax_year", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_tax_lot_matches_purchase_transaction_id",
                table: "tax_lot_match_snapshots",
                column: "purchase_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_tax_lot_matches_sale_transaction_id",
                table: "tax_lot_match_snapshots",
                column: "sale_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ux_tax_lot_matches_run_purchase_sale",
                table: "tax_lot_match_snapshots",
                columns: new[] { "tax_calculation_run_id", "purchase_transaction_id", "sale_transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_accounts_normalized_email",
                table: "user_accounts",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_splits");

            migrationBuilder.DropTable(
                name: "tax_lot_match_snapshots");

            migrationBuilder.DropTable(
                name: "tax_calculation_runs");

            migrationBuilder.DropTable(
                name: "investment_transactions");

            migrationBuilder.DropTable(
                name: "exchange_rates");

            migrationBuilder.DropTable(
                name: "instruments");

            migrationBuilder.DropTable(
                name: "portfolios");

            migrationBuilder.DropTable(
                name: "user_accounts");
        }
    }
}