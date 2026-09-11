using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapKino.Api.Migrations
{
    /// <inheritdoc />
    public partial class FilmstripUx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminPriority",
                table: "Filmstrips");

            migrationBuilder.DropColumn(
                name: "CoverReferenceTmdbId",
                table: "Filmstrips");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdminPriority",
                table: "Filmstrips",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CoverReferenceTmdbId",
                table: "Filmstrips",
                type: "integer",
                nullable: true);
        }
    }
}
