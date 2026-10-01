using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiadHelper.Core.Migrations
{
    /// <inheritdoc />
    public partial class VoidMiscForums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MiscFilingThreadId",
                table: "Cases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VoidRequestThreadId",
                table: "Cases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "VoidRequestedById",
                table: "Cases",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MiscFilingThreadId",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "VoidRequestThreadId",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "VoidRequestedById",
                table: "Cases");
        }
    }
}
