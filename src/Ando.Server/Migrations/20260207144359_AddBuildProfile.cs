using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ando.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Profile",
                table: "Builds",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Profile",
                table: "Builds");
        }
    }
}
