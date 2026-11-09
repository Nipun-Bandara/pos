using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CheckoutAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ManualPriceApprovedBy",
                table: "SaleLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualPriceReason",
                table: "SaleLines",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NormalUnitPriceMinor",
                table: "SaleLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualifiedPriceBand",
                table: "SaleLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QualifiedTierUnitPriceMinor",
                table: "SaleLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingBaskets",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShopId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TerminalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingBaskets", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingBaskets");

            migrationBuilder.DropColumn(
                name: "ManualPriceApprovedBy",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "ManualPriceReason",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "NormalUnitPriceMinor",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "QualifiedPriceBand",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "QualifiedTierUnitPriceMinor",
                table: "SaleLines");
        }
    }
}
