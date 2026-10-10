using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STNG.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationOtps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbpUsers_NormalizedEmail",
                table: "AbpUsers");

            migrationBuilder.CreateTable(
                name: "AppEmailVerificationOtps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppEmailVerificationOtps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppEmailVerificationOtps_AbpUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AbpUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpUsers_NormalizedEmail",
                table: "AbpUsers",
                column: "NormalizedEmail",
                unique: true,
                filter: "[IsDeleted] = 0 AND [TenantId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppEmailVerificationOtps_UserId_Purpose",
                table: "AppEmailVerificationOtps",
                columns: new[] { "UserId", "Purpose" },
                unique: true,
                filter: "[UsedAt] IS NULL AND [InvalidatedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppEmailVerificationOtps_UserId_Purpose_LastSentAt",
                table: "AppEmailVerificationOtps",
                columns: new[] { "UserId", "Purpose", "LastSentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppEmailVerificationOtps");

            migrationBuilder.DropIndex(
                name: "IX_AbpUsers_NormalizedEmail",
                table: "AbpUsers");

            migrationBuilder.CreateIndex(
                name: "IX_AbpUsers_NormalizedEmail",
                table: "AbpUsers",
                column: "NormalizedEmail");
        }
    }
}
