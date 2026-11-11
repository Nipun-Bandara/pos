using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class DailyCloseAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryIdSnapshot",
                table: "SaleLines",
                type: "TEXT",
                nullable: true);
            migrationBuilder.Sql("CREATE TRIGGER CashMovements_NoUpdate BEFORE UPDATE ON CashMovements BEGIN SELECT RAISE(ABORT,'Cash movements are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER CashMovements_NoDelete BEFORE DELETE ON CashMovements BEGIN SELECT RAISE(ABORT,'Cash movements are append-only'); END;");
            migrationBuilder.Sql("CREATE TRIGGER CashMovements_RequireOpenShift BEFORE INSERT ON CashMovements WHEN NOT EXISTS(SELECT 1 FROM Shifts WHERE Id=NEW.ShiftId AND ShopId=NEW.ShopId AND Status='Open') BEGIN SELECT RAISE(ABORT,'Cash movement requires open shift'); END;");
            migrationBuilder.Sql("CREATE TRIGGER Shifts_ClosedNoUpdate BEFORE UPDATE ON Shifts WHEN OLD.Status='Closed' BEGIN SELECT RAISE(ABORT,'Closed shifts are immutable'); END;");
            migrationBuilder.Sql("CREATE TRIGGER Shifts_NoDelete BEFORE DELETE ON Shifts BEGIN SELECT RAISE(ABORT,'Shifts cannot be deleted'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Daily cash evidence uses forward-only migrations.");
        }
    }
}
