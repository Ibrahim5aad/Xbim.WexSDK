using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xbim.WexServer.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OAuthApps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ClientType = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ClientSecretHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    RedirectUris = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AllowedScopes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthApps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OAuthApps_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OAuthApps_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OAuthAppAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OAuthAppId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthAppAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OAuthAppAuditLogs_OAuthApps_OAuthAppId",
                        column: x => x.OAuthAppId,
                        principalTable: "OAuthApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OAuthAppAuditLogs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAppAuditLogs_ActorUserId",
                table: "OAuthAppAuditLogs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAppAuditLogs_OAuthAppId",
                table: "OAuthAppAuditLogs",
                column: "OAuthAppId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthAppAuditLogs_Timestamp",
                table: "OAuthAppAuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthApps_ClientId",
                table: "OAuthApps",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OAuthApps_CreatedByUserId",
                table: "OAuthApps",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthApps_WorkspaceId",
                table: "OAuthApps",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OAuthAppAuditLogs");

            migrationBuilder.DropTable(
                name: "OAuthApps");
        }
    }
}
