using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapKino.Api.Migrations
{
    /// <inheritdoc />
    public partial class GorseRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecommendationSyncedAt",
                table: "Movies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MovieThemeMemberships",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "integer", nullable: false),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    ThemeSlug = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    ThemeVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieThemeMemberships", x => new { x.TmdbId, x.IsSeries, x.ThemeSlug });
                    table.ForeignKey(
                        name: "FK_MovieThemeMemberships_Movies_TmdbId_IsSeries",
                        columns: x => new { x.TmdbId, x.IsSeries },
                        principalTable: "Movies",
                        principalColumns: new[] { "TmdbId", "IsSeries" },
                        onDelete: ReferentialAction.Cascade);
                });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovieThemeMemberships");

            migrationBuilder.DropColumn(
                name: "RecommendationSyncedAt",
                table: "Movies");
        }
    }
}
