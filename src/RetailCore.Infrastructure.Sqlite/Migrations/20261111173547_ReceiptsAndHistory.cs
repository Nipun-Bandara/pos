using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptsAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceiptDocuments",
                columns: table => new
                {
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShopId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptDocuments", x => x.SaleId);
                    table.ForeignKey(
                        name: "FK_ReceiptDocuments_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptPrintAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShopId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptPrintAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptPrintAttempts_ReceiptDocuments_SaleId",
                        column: x => x.SaleId,
                        principalTable: "ReceiptDocuments",
                        principalColumn: "SaleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptPrintOutcomes",
                columns: table => new
                {
                    AttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptPrintOutcomes", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_ReceiptPrintOutcomes_ReceiptPrintAttempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "ReceiptPrintAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptDocuments_ShopId",
                table: "ReceiptDocuments",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPrintAttempts_SaleId",
                table: "ReceiptPrintAttempts",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPrintAttempts_ShopId_SaleId",
                table: "ReceiptPrintAttempts",
                columns: new[] { "ShopId", "SaleId" });
            foreach (var table in new[] { "ReceiptDocuments", "ReceiptPrintAttempts", "ReceiptPrintOutcomes" })
            {
                migrationBuilder.Sql($"CREATE TRIGGER {table}_NoUpdate BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT, 'Receipt records are append-only'); END;");
                migrationBuilder.Sql($"CREATE TRIGGER {table}_NoDelete BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT, 'Receipt records are append-only'); END;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceiptPrintOutcomes");

            migrationBuilder.DropTable(
                name: "ReceiptPrintAttempts");

            migrationBuilder.DropTable(
                name: "ReceiptDocuments");
        }
    }
}
