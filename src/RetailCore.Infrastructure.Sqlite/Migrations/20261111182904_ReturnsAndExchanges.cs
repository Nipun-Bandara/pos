using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ReturnsAndExchanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExchangeCardCreditMinor",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ExchangeCashCreditMinor",
                table: "Sales",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeRefundId",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CardCreditMinor",
                table: "Refunds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CardPaidMinor",
                table: "Refunds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CashCreditMinor",
                table: "Refunds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CashPaidMinor",
                table: "Refunds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ExchangeCreditMinor",
                table: "Refunds",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCardReference",
                table: "Refunds",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoidReversal",
                table: "Refunds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacementSaleId",
                table: "Refunds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReturnDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShopId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalSaleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnDocuments_Sales_OriginalSaleId",
                        column: x => x.OriginalSaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnPrintAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShopId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnPrintAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnPrintAttempts_ReturnDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "ReturnDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReturnPrintOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnPrintOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnPrintOutcomes_ReturnPrintAttempts_Id",
                        column: x => x.Id,
                        principalTable: "ReturnPrintAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnDocuments_OriginalSaleId",
                table: "ReturnDocuments",
                column: "OriginalSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnDocuments_ShopId_OriginalSaleId",
                table: "ReturnDocuments",
                columns: new[] { "ShopId", "OriginalSaleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnPrintAttempts_DocumentId",
                table: "ReturnPrintAttempts",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnPrintAttempts_ShopId_DocumentId",
                table: "ReturnPrintAttempts",
                columns: new[] { "ShopId", "DocumentId" });
            foreach(var table in new[]{"ReturnDocuments","ReturnPrintAttempts","ReturnPrintOutcomes"})
            {
                migrationBuilder.Sql($"CREATE TRIGGER {table}_NoUpdate BEFORE UPDATE ON {table} BEGIN SELECT RAISE(ABORT,'Return records are append-only'); END;");
                migrationBuilder.Sql($"CREATE TRIGGER {table}_NoDelete BEFORE DELETE ON {table} BEGIN SELECT RAISE(ABORT,'Return records are append-only'); END;");
            }
            migrationBuilder.Sql("CREATE TRIGGER Sales_NoVoidAfterReturn BEFORE UPDATE OF Status ON Sales WHEN NEW.Status='Voided' AND OLD.Status='Completed' AND EXISTS(SELECT 1 FROM Refunds WHERE OriginalSaleId=OLD.Id AND CompletedAt IS NOT NULL AND IsVoidReversal=0) BEGIN SELECT RAISE(ABORT,'A returned sale cannot be voided'); END;");
            migrationBuilder.Sql("CREATE TRIGGER Refunds_NoReturnAfterVoid BEFORE INSERT ON Refunds WHEN NEW.IsVoidReversal=0 AND EXISTS(SELECT 1 FROM Sales WHERE Id=NEW.OriginalSaleId AND Status='Voided') BEGIN SELECT RAISE(ABORT,'A voided sale cannot be returned'); END;");
            migrationBuilder.Sql("CREATE TRIGGER Refunds_CompletedNoUpdate BEFORE UPDATE ON Refunds WHEN OLD.CompletedAt IS NOT NULL BEGIN SELECT RAISE(ABORT,'Completed refunds are immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER Refunds_CompletedNoDelete BEFORE DELETE ON Refunds WHEN OLD.CompletedAt IS NOT NULL BEGIN SELECT RAISE(ABORT,'Completed refunds are immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER RefundLines_CompletedNoUpdate BEFORE UPDATE ON RefundLines WHEN EXISTS(SELECT 1 FROM Refunds WHERE Id=OLD.RefundId AND CompletedAt IS NOT NULL) BEGIN SELECT RAISE(ABORT,'Completed refunds are immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER RefundLines_CompletedNoDelete BEFORE DELETE ON RefundLines WHEN EXISTS(SELECT 1 FROM Refunds WHERE Id=OLD.RefundId AND CompletedAt IS NOT NULL) BEGIN SELECT RAISE(ABORT,'Completed refunds are immutable'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnPrintOutcomes");

            migrationBuilder.DropTable(
                name: "ReturnPrintAttempts");

            migrationBuilder.DropTable(
                name: "ReturnDocuments");

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
