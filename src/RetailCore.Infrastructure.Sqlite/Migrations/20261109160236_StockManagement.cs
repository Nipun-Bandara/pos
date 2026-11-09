using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class StockManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "StockMovements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUnit",
                table: "StockMovements",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConversionFactorThousandths",
                table: "StockMovements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000L);

            migrationBuilder.AddColumn<Guid>(
                name: "EnteredProductId",
                table: "StockMovements",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InputCostCurrency",
                table: "StockMovements",
                type: "TEXT",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "InputQuantityThousandths",
                table: "StockMovements",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "InputUnitCostMinor",
                table: "StockMovements",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostingKey",
                table: "StockMovements",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "StockMovements",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StockBaseUnit",
                table: "SaleLines",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StockFactorThousandths",
                table: "SaleLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000L);

            migrationBuilder.AddColumn<Guid>(
                name: "StockProductId",
                table: "SaleLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCurrency",
                table: "RefundLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Disposition",
                table: "RefundLines",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Saleable");

            migrationBuilder.AddColumn<string>(
                name: "StockBaseUnit",
                table: "RefundLines",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StockFactorThousandths",
                table: "RefundLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000L);

            migrationBuilder.AddColumn<Guid>(
                name: "StockProductId",
                table: "RefundLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UnitCostMinor",
                table: "RefundLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "StockBaseProductId",
                table: "Products",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StockBaseUnit",
                table: "Products",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "StockFactorThousandths",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000L);

            migrationBuilder.CreateTable(
                name: "StockPostingReceipts",
                columns: table => new
                {
                    ShopId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
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
            migrationBuilder.Sql("CREATE TRIGGER StockPostingReceipts_NoUpdate BEFORE UPDATE ON StockPostingReceipts BEGIN SELECT RAISE(ABORT, 'Stock posting receipts are immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER StockPostingReceipts_NoDelete BEFORE DELETE ON StockPostingReceipts BEGIN SELECT RAISE(ABORT, 'Stock posting receipts are immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER StockMovements_ValidatePosting BEFORE INSERT ON StockMovements WHEN NEW.PostingKey IS NOT NULL AND (NEW.QuantityThousandths <= 0 OR NEW.InputQuantityThousandths <= 0 OR NEW.ConversionFactorThousandths <= 0 OR NEW.InputUnitCostMinor < 0 OR NEW.InputUnitCostMinor IS NULL OR NEW.InputCostCurrency IS NULL OR NEW.EnteredProductId IS NULL OR NEW.BaseUnit IS NULL) BEGIN SELECT RAISE(ABORT, 'Invalid stock posting snapshot'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS StockPostingReceipts_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS StockPostingReceipts_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS StockMovements_ValidatePosting;");
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
