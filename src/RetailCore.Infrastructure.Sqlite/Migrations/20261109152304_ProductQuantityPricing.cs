using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ProductQuantityPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AppliedMinimumThousandths",
                table: "SaleLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AppliedPriceBand",
                table: "SaleLines",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnglishNameSnapshot",
                table: "SaleLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNameSnapshot",
                table: "SaleLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SinhalaNameSnapshot",
                table: "SaleLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBarcode",
                table: "Products",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptName",
                table: "Products",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SinhalaName",
                table: "Products",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SinhalaName",
                table: "Categories",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // No invented translations: legacy catalogue stays available for owner correction,
            // but cannot be sold as active until Sinhala receipt data is supplied.
            migrationBuilder.Sql("UPDATE Products SET IsActive = 0;");

            migrationBuilder.CreateTable(
                name: "ProductPriceBands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MinimumThousandths = table.Column<long>(type: "INTEGER", nullable: false),
                    PriceMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPriceBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPriceBands_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_PrimaryBarcode",
                table: "Products",
                column: "PrimaryBarcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceBands_ProductId_MinimumThousandths",
                table: "ProductPriceBands",
                columns: new[] { "ProductId", "MinimumThousandths" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductPriceBands");

            migrationBuilder.DropIndex(
                name: "IX_Products_PrimaryBarcode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "AppliedMinimumThousandths",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "AppliedPriceBand",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "EnglishNameSnapshot",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "ReceiptNameSnapshot",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "SinhalaNameSnapshot",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "PrimaryBarcode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReceiptName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SinhalaName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SinhalaName",
                table: "Categories");
        }
    }
}
