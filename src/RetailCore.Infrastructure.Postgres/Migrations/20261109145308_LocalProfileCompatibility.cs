using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class LocalProfileCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<long>(
                name: "CashIncrementMinor",
                table: "Shops",
                type: "bigint",
                nullable: false,
                defaultValue: 100L);

            migrationBuilder.AddColumn<int>(
                name: "PaperWidthMillimeters",
                table: "Shops",
                type: "integer",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.AddColumn<string>(
                name: "PrinterName",
                table: "Shops",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptFooter",
                table: "Shops",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CashIncrementMinor",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "PaperWidthMillimeters",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "PrinterName",
                table: "Shops");

            migrationBuilder.DropColumn(
                name: "ReceiptFooter",
                table: "Shops");
        }
    }
}
