using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace STNG.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppFamilies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ImageFileAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreationRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppFamilies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppFamilies_AppParents_CreatedByParentId",
                        column: x => x.CreatedByParentId,
                        principalTable: "AppParents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppFamilyMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeftAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemovedByParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppFamilyMembers", x => x.Id);
                    table.UniqueConstraint("AK_AppFamilyMembers_FamilyId_ParentId", x => new { x.FamilyId, x.ParentId });
                    table.ForeignKey(
                        name: "FK_AppFamilyMembers_AppFamilies_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "AppFamilies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyMembers_AppParents_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AppParents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyMembers_AppParents_RemovedByParentId",
                        column: x => x.RemovedByParentId,
                        principalTable: "AppParents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppFamilyMembershipHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Change = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppFamilyMembershipHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppFamilyMembershipHistory_AppFamilies_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "AppFamilies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyMembershipHistory_AppFamilyMembers_MemberId",
                        column: x => x.MemberId,
                        principalTable: "AppFamilyMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyMembershipHistory_AppParents_ActorParentId",
                        column: x => x.ActorParentId,
                        principalTable: "AppParents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppFamilyMembershipHistory_AppParents_ParentId",
                        column: x => x.ParentId,
                        principalTable: "AppParents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppOwnershipTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FamilyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOwnershipTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppOwnershipTransfers_AppFamilies_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "AppFamilies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppOwnershipTransfers_AppFamilyMembers_FamilyId_FromParentId",
                        columns: x => new { x.FamilyId, x.FromParentId },
                        principalTable: "AppFamilyMembers",
                        principalColumns: new[] { "FamilyId", "ParentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppOwnershipTransfers_AppFamilyMembers_FamilyId_ToParentId",
                        columns: x => new { x.FamilyId, x.ToParentId },
                        principalTable: "AppFamilyMembers",
                        principalColumns: new[] { "FamilyId", "ParentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilies_CreatedByParentId_CreationRequestId",
                table: "AppFamilies",
                columns: new[] { "CreatedByParentId", "CreationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyMembers_ActiveOwner",
                table: "AppFamilyMembers",
                column: "FamilyId",
                unique: true,
                filter: "[Role] = 0 AND [Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyMembers_ParentId_Status",
                table: "AppFamilyMembers",
                columns: new[] { "ParentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyMembers_RemovedByParentId",
                table: "AppFamilyMembers",
                column: "RemovedByParentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyMembershipHistory_ActorParentId",
                table: "AppFamilyMembershipHistory",
                column: "ActorParentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyMembershipHistory_FamilyId_OccurredAt",
                table: "AppFamilyMembershipHistory",
                columns: new[] { "FamilyId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyMembershipHistory_MemberId",
                table: "AppFamilyMembershipHistory",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_AppFamilyMembershipHistory_ParentId",
                table: "AppFamilyMembershipHistory",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOwnershipTransfers_FamilyId_FromParentId",
                table: "AppOwnershipTransfers",
                columns: new[] { "FamilyId", "FromParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppOwnershipTransfers_FamilyId_ToParentId",
                table: "AppOwnershipTransfers",
                columns: new[] { "FamilyId", "ToParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppOwnershipTransfers_Pending",
                table: "AppOwnershipTransfers",
                column: "FamilyId",
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppFamilyMembershipHistory");

            migrationBuilder.DropTable(
                name: "AppOwnershipTransfers");

            migrationBuilder.DropTable(
                name: "AppFamilyMembers");

            migrationBuilder.DropTable(
                name: "AppFamilies");
        }
    }
}
