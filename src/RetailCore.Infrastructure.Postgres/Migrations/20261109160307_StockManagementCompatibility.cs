using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class StockManagementCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "StockMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUnit",
                table: "StockMovements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConversionFactorThousandths",
                table: "StockMovements",
                type: "bigint",
                nullable: false,
                defaultValue: 1000L);

            migrationBuilder.AddColumn<Guid>(
                name: "EnteredProductId",
                table: "StockMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputCostCurrency",
                table: "StockMovements",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "InputQuantityThousandths",
                table: "StockMovements",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "InputUnitCostMinor",
                table: "StockMovements",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostingKey",
                table: "StockMovements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "StockMovements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StockBaseUnit",
                table: "SaleLines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StockFactorThousandths",
                table: "SaleLines",
                type: "bigint",
                nullable: false,
                defaultValue: 1000L);

            migrationBuilder.AddColumn<Guid>(
                name: "StockProductId",
                table: "SaleLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCurrency",
                table: "RefundLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Disposition",
                table: "RefundLines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Saleable");

            migrationBuilder.AddColumn<string>(
                name: "StockBaseUnit",
                table: "RefundLines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StockFactorThousandths",
                table: "RefundLines",
                type: "bigint",
                nullable: false,
                defaultValue: 1000L);

            migrationBuilder.AddColumn<Guid>(
                name: "StockProductId",
                table: "RefundLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UnitCostMinor",
                table: "RefundLines",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "StockBaseProductId",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StockBaseUnit",
                table: "Products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StockFactorThousandths",
                table: "Products",
                type: "bigint",
                nullable: false,
                defaultValue: 1000L);

            migrationBuilder.CreateTable(
                name: "StockPostingReceipts",
                columns: table => new
                {
                    ShopId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockPostingReceipts", x => new { x.ShopId, x.SourceType, x.SourceId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PostingKey",
                table: "StockMovements",
                column: "PostingKey",
                unique: true);
            migrationBuilder.Sql("CREATE TRIGGER stock_posting_receipts_immutable BEFORE UPDATE OR DELETE ON \"StockPostingReceipts\" FOR EACH ROW EXECUTE FUNCTION app_forbid_change();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockPostingReceipts");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_PostingKey",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BaseUnit",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ConversionFactorThousandths",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "EnteredProductId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "InputCostCurrency",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "InputQuantityThousandths",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "InputUnitCostMinor",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "PostingKey",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "StockBaseUnit",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "StockFactorThousandths",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "StockProductId",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "CostCurrency",
                table: "RefundLines");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "RefundLines");

            migrationBuilder.DropColumn(
                name: "StockBaseUnit",
                table: "RefundLines");

            migrationBuilder.DropColumn(
                name: "StockFactorThousandths",
                table: "RefundLines");

            migrationBuilder.DropColumn(
                name: "StockProductId",
                table: "RefundLines");

            migrationBuilder.DropColumn(
                name: "UnitCostMinor",
                table: "RefundLines");

            migrationBuilder.DropColumn(
                name: "StockBaseProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StockBaseUnit",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StockFactorThousandths",
                table: "Products");
        }
    }
}
