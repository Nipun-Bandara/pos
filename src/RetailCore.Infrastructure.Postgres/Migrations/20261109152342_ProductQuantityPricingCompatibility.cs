using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailCore.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ProductQuantityPricingCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AppliedMinimumThousandths",
                table: "SaleLines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AppliedPriceBand",
                table: "SaleLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnglishNameSnapshot",
                table: "SaleLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNameSnapshot",
                table: "SaleLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SinhalaNameSnapshot",
                table: "SaleLines",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBarcode",
                table: "Products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptName",
                table: "Products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SinhalaName",
                table: "Products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SinhalaName",
                table: "Categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE \"Products\" SET \"IsActive\" = FALSE;");

            migrationBuilder.CreateTable(
                name: "ProductPriceBands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MinimumThousandths = table.Column<long>(type: "bigint", nullable: false),
                    PriceMinor = table.Column<long>(type: "bigint", nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false)
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
