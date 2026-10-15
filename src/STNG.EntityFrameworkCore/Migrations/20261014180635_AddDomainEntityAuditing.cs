using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STNG.Migrations
{
    /// <inheritdoc />
    public partial class AddDomainEntityAuditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeleterId",
                table: "AppParents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionTime",
                table: "AppParents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AppParents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                table: "AppParents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifierId",
                table: "AppParents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModificationTime",
                table: "AppEmailVerificationOtps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastModifierId",
                table: "AppEmailVerificationOtps",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeleterId",
                table: "AppParents");

            migrationBuilder.DropColumn(
                name: "DeletionTime",
                table: "AppParents");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AppParents");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                table: "AppParents");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                table: "AppParents");

            migrationBuilder.DropColumn(
                name: "LastModificationTime",
                table: "AppEmailVerificationOtps");

            migrationBuilder.DropColumn(
                name: "LastModifierId",
                table: "AppEmailVerificationOtps");
        }
    }
}
