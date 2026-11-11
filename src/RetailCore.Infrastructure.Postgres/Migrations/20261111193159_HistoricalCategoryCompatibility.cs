using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalCategoryCompatibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryIdSnapshot",
                table: "SaleLines",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoryIdSnapshot",
                table: "SaleLines");
        }
    }
}
