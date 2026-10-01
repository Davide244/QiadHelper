using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiadHelper.Core.Migrations
{
    /// <inheritdoc />
    public partial class QueueAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "QueueChannelId",
                table: "Cases",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "QueueMessageId",
                table: "Cases",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QueueChannelId",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "QueueMessageId",
                table: "Cases");
        }
    }
}
