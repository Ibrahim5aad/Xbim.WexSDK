using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Octopus.Server.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadModeToUploadSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectUploadUrl",
                table: "UploadSessions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UploadMode",
                table: "UploadSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DirectUploadUrl",
                table: "UploadSessions");

            migrationBuilder.DropColumn(
                name: "UploadMode",
                table: "UploadSessions");
        }
    }
}
