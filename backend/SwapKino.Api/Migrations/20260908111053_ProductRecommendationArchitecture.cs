using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SwapKino.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProductRecommendationArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Filmstrips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AdminPriority = table.Column<int>(type: "integer", nullable: false),
                    CoverReferenceTmdbId = table.Column<int>(type: "integer", nullable: true),
                    ConfigVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Filmstrips", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeywordAliases",
                columns: table => new
                {
                    TmdbKeywordId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RuName = table.Column<string>(type: "text", nullable: false),
                    OriginalName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeywordAliases", x => x.TmdbKeywordId);
                });

            migrationBuilder.CreateTable(
                name: "UserTasteFeatures",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureType = table.Column<int>(type: "integer", nullable: false),
                    TmdbFeatureId = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    ObservationCount = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTasteFeatures", x => new { x.UserId, x.FeatureType, x.TmdbFeatureId });
                });

            migrationBuilder.CreateTable(
                name: "FilmstripFeatures",
                columns: table => new
                {
                    FilmstripId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureType = table.Column<int>(type: "integer", nullable: false),
                    TmdbFeatureId = table.Column<int>(type: "integer", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmstripFeatures", x => new { x.FilmstripId, x.FeatureType, x.TmdbFeatureId });
                    table.ForeignKey(
                        name: "FK_FilmstripFeatures_Filmstrips_FilmstripId",
                        column: x => x.FilmstripId,
                        principalTable: "Filmstrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FilmstripReferences",
                columns: table => new
                {
                    FilmstripId = table.Column<Guid>(type: "uuid", nullable: false),
                    TmdbId = table.Column<int>(type: "integer", nullable: false),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilmstripReferences", x => new { x.FilmstripId, x.TmdbId, x.IsSeries });
                    table.ForeignKey(
                        name: "FK_FilmstripReferences_Filmstrips_FilmstripId",
                        column: x => x.FilmstripId,
                        principalTable: "Filmstrips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Filmstrips_Slug",
                table: "Filmstrips",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTasteFeatures_UserId_Weight",
                table: "UserTasteFeatures",
                columns: new[] { "UserId", "Weight" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilmstripFeatures");

            migrationBuilder.DropTable(
                name: "FilmstripReferences");

            migrationBuilder.DropTable(
                name: "KeywordAliases");

            migrationBuilder.DropTable(
                name: "UserTasteFeatures");

            migrationBuilder.DropTable(
                name: "Filmstrips");

        }
    }
}
