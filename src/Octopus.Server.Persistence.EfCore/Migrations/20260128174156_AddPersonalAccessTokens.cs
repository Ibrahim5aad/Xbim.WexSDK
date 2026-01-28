using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octopus.Server.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonalAccessTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    TokenPrefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Scopes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastUsedIpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedFromIpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalAccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonalAccessTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonalAccessTokens_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PersonalAccessTokenAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonalAccessTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalAccessTokenAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonalAccessTokenAuditLogs_PersonalAccessTokens_PersonalAccessTokenId",
                        column: x => x.PersonalAccessTokenId,
                        principalTable: "PersonalAccessTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonalAccessTokenAuditLogs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokenAuditLogs_ActorUserId",
                table: "PersonalAccessTokenAuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokenAuditLogs_PersonalAccessTokenId",
                table: "PersonalAccessTokenAuditLogs",
                column: "PersonalAccessTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokenAuditLogs_Timestamp",
                table: "PersonalAccessTokenAuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_ExpiresAt",
                table: "PersonalAccessTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_TokenHash",
                table: "PersonalAccessTokens",
                column: "TokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_UserId",
                table: "PersonalAccessTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_UserId_WorkspaceId_IsRevoked",
                table: "PersonalAccessTokens",
                columns: new[] { "UserId", "WorkspaceId", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_WorkspaceId",
                table: "PersonalAccessTokens",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonalAccessTokenAuditLogs");

            migrationBuilder.DropTable(
                name: "PersonalAccessTokens");
        }
    }
}
