using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QiadHelper.Core.Migrations
{
    /// <inheritdoc />
    public partial class UnfairFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecondaryCategoryId",
                table: "CaseCategories");

            migrationBuilder.RenameColumn(
                name: "SecondaryButtonLabel",
                table: "CaseCategories",
                newName: "UnfairButtonLabel");

            migrationBuilder.AddColumn<bool>(
                name: "IsUnfair",
                table: "Cases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OfferUnfairAppeals",
                table: "CaseCategories",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsUnfair",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "OfferUnfairAppeals",
                table: "CaseCategories");

            migrationBuilder.RenameColumn(
                name: "UnfairButtonLabel",
                table: "CaseCategories",
                newName: "SecondaryButtonLabel");

            migrationBuilder.AddColumn<Guid>(
                name: "SecondaryCategoryId",
                table: "CaseCategories",
                type: "uuid",
                nullable: true);
        }
    }
}
