using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ando.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildGitVersionTag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GitVersionTag",
                table: "Builds",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitVersionTag",
                table: "Builds");
        }
    }
}
