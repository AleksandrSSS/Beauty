using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Beauty.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterPhotoUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhotoUrl",
                table: "Masters",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoUrl",
                table: "Masters");
        }
    }
}
