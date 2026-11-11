using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class CheckoutPriceCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ManualPriceApprovedBy",
                table: "SaleLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualPriceReason",
                table: "SaleLines",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NormalUnitPriceMinor",
                table: "SaleLines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualifiedPriceBand",
                table: "SaleLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QualifiedTierUnitPriceMinor",
                table: "SaleLines",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
