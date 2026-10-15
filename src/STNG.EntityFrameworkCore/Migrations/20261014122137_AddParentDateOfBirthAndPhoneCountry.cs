using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STNG.Migrations
{
    /// <inheritdoc />
    public partial class AddParentDateOfBirthAndPhoneCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "AppParents",
                type: "date",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "PhoneCountryIso2",
                table: "AppParents",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "AppParents");

            migrationBuilder.DropColumn(
                name: "PhoneCountryIso2",
                table: "AppParents");
        }
    }
}
