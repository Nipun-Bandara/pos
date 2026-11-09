using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class LocalFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Users",
                type: "TEXT",
                maxLength: 2,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<long>(
                name: "CashIncrementMinor",
                table: "Shops",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100L);

            migrationBuilder.AddColumn<int>(
                name: "PaperWidthMillimeters",
                table: "Shops",
                type: "INTEGER",
                nullable: false,
                defaultValue: 80);

            migrationBuilder.AddColumn<string>(
                name: "PrinterName",
                table: "Shops",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptFooter",
                table: "Shops",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LocalCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalCommands", x => x.Id);
                });
            // Local append-only evidence. Corrections must append compensating records.
            migrationBuilder.Sql("CREATE TRIGGER AuditEntries_NoUpdate BEFORE UPDATE ON AuditEntries BEGIN SELECT RAISE(ABORT, 'Audit entries are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER AuditEntries_NoDelete BEFORE DELETE ON AuditEntries BEGIN SELECT RAISE(ABORT, 'Audit entries are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER StockMovements_NoUpdate BEFORE UPDATE ON StockMovements BEGIN SELECT RAISE(ABORT, 'Stock movements are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER StockMovements_NoDelete BEFORE DELETE ON StockMovements BEGIN SELECT RAISE(ABORT, 'Stock movements are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER LocalCommands_NoUpdate BEFORE UPDATE ON LocalCommands BEGIN SELECT RAISE(ABORT, 'Command receipts are immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER LocalCommands_NoDelete BEFORE DELETE ON LocalCommands BEGIN SELECT RAISE(ABORT, 'Command receipts are immutable'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS AuditEntries_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS AuditEntries_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS StockMovements_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS StockMovements_NoDelete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS LocalCommands_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS LocalCommands_NoDelete;");
            migrationBuilder.DropTable(
                name: "LocalCommands");

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
