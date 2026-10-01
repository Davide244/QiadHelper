using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiadHelper.Core.Migrations
{
    /// <inheritdoc />
    public partial class ForumFormatFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalEvidenceUrl",
                table: "Cases",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestContext",
                table: "Cases",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidReason",
                table: "Cases",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalEvidenceUrl",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "RequestContext",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Cases");
        }
    }
}
