using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ReturnSettlementCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExchangeCardCreditMinor",
                table: "Sales",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ExchangeCashCreditMinor",
                table: "Sales",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRefundId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CardCreditMinor",
                table: "Refunds",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CardPaidMinor",
                table: "Refunds",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CashCreditMinor",
                table: "Refunds",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CashPaidMinor",
                table: "Refunds",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ExchangeCreditMinor",
                table: "Refunds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCardReference",
                table: "Refunds",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoidReversal",
                table: "Refunds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacementSaleId",
                table: "Refunds",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeCardCreditMinor",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ExchangeCashCreditMinor",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ExchangeRefundId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CardCreditMinor",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "CardPaidMinor",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "CashCreditMinor",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "CashPaidMinor",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "ExchangeCreditMinor",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "ExternalCardReference",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "IsVoidReversal",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "ReplacementSaleId",
                table: "Refunds");
        }
    }
}
