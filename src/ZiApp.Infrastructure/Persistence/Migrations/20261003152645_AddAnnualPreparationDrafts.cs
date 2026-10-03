using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZiApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualPreparationDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "annual_preparation_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_year = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    schema_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    snapshot_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_annual_preparation_drafts", x => x.id);
                    table.CheckConstraint("ck_annual_preparation_hash", "snapshot_sha256 ~ '^[0-9A-F]{64}$'");
                    table.CheckConstraint("ck_annual_preparation_year", "tax_year = 2025");
                    table.ForeignKey(
                        name: "FK_annual_preparation_drafts_user_accounts_owner_account_id",
                        column: x => x.owner_account_id,
                        principalTable: "user_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_annual_preparation_drafts_owner_account_id_tax_year_created~",
                table: "annual_preparation_drafts",
                columns: new[] { "owner_account_id", "tax_year", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM annual_preparation_drafts) THEN
                        RAISE EXCEPTION 'Downgrade would erase saved annual preparation drafts. Use a forward migration.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "annual_preparation_drafts");
        }
    }
}
