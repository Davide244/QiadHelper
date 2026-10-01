using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiadHelper.Core.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "RejectionRestrictionDays",
                table: "CaseCategories",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RejectionRestrictionRoleId",
                table: "CaseCategories",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectionRestrictionDays",
                table: "CaseCategories");

            migrationBuilder.DropColumn(
                name: "RejectionRestrictionRoleId",
                table: "CaseCategories");
        }
    }
}
