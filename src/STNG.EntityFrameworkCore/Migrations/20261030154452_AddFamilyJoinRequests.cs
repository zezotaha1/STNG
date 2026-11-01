using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STNG.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyJoinRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppFamilyJoinCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SecretHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppFamilyJoinCredentials", x => x.Id);
                    table.UniqueConstraint("AK_AppFamilyJoinCredentials_FamilyId_Id", x => new { x.FamilyId, x.Id });
                    table.ForeignKey(
                        name: "FK_AppFamilyJoinCredentials_AppFamilies_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "AppFamilies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyJoinCredentials_AppFamilyMembers_FamilyId_CreatedByParentId",
                        columns: x => new { x.FamilyId, x.CreatedByParentId },
                        principalTable: "AppFamilyMembers",
                        principalColumns: new[] { "FamilyId", "ParentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyJoinCredentials_AppFamilyMembers_FamilyId_RevokedByParentId",
                        columns: x => new { x.FamilyId, x.RevokedByParentId },
                        principalTable: "AppFamilyMembers",
                        principalColumns: new[] { "FamilyId", "ParentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppFamilyJoinRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppFamilyJoinRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppFamilyJoinRequests_AppFamilies_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "AppFamilies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyJoinRequests_AppFamilyJoinCredentials_FamilyId_CredentialId",
                        columns: x => new { x.FamilyId, x.CredentialId },
                        principalTable: "AppFamilyJoinCredentials",
                        principalColumns: new[] { "FamilyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyJoinRequests_AppFamilyMembers_FamilyId_ReviewedByParentId",
                        columns: x => new { x.FamilyId, x.ReviewedByParentId },
                        principalTable: "AppFamilyMembers",
                        principalColumns: new[] { "FamilyId", "ParentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyJoinRequests_AppParents_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AppParents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinCredentials_ActiveKind",
                table: "AppFamilyJoinCredentials",
                columns: new[] { "FamilyId", "Kind" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinCredentials_FamilyId_CreatedAt_Id",
                table: "AppFamilyJoinCredentials",
                columns: new[] { "FamilyId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinCredentials_FamilyId_CreatedByParentId",
                table: "AppFamilyJoinCredentials",
                columns: new[] { "FamilyId", "CreatedByParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinCredentials_FamilyId_RevokedByParentId",
                table: "AppFamilyJoinCredentials",
                columns: new[] { "FamilyId", "RevokedByParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinCredentials_Kind_SecretHash",
                table: "AppFamilyJoinCredentials",
                columns: new[] { "Kind", "SecretHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinRequests_CredentialId_Status",
                table: "AppFamilyJoinRequests",
                columns: new[] { "CredentialId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinRequests_FamilyId_CredentialId",
                table: "AppFamilyJoinRequests",
                columns: new[] { "FamilyId", "CredentialId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinRequests_FamilyId_ReviewedByParentId",
                table: "AppFamilyJoinRequests",
                columns: new[] { "FamilyId", "ReviewedByParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinRequests_FamilyId_Status_RequestedAt_Id",
                table: "AppFamilyJoinRequests",
                columns: new[] { "FamilyId", "Status", "RequestedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinRequests_ParentId_RequestedAt_Id",
                table: "AppFamilyJoinRequests",
                columns: new[] { "ParentId", "RequestedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyJoinRequests_PendingParent",
                table: "AppFamilyJoinRequests",
                columns: new[] { "FamilyId", "ParentId" },
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppFamilyJoinRequests");

            migrationBuilder.DropTable(
                name: "AppFamilyJoinCredentials");
        }
    }
}
