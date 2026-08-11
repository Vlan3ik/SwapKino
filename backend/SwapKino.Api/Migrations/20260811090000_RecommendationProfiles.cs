using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SwapKino.Api;

#nullable disable

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("20260811090000_RecommendationProfiles")]
public partial class RecommendationProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector");
        migrationBuilder.CreateTable(
            name: "MovieRecommendationFeatures",
            columns: table => new
            {
                TmdbId = table.Column<int>(type: "integer", nullable: false),
                IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                FeatureJson = table.Column<string>(type: "text", nullable: false),
                FeatureVersion = table.Column<string>(type: "text", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MovieRecommendationFeatures", x => new { x.TmdbId, x.IsSeries });
                table.ForeignKey("FK_MovieRecommendationFeatures_Movies_TmdbId_IsSeries", x => new { x.TmdbId, x.IsSeries }, "Movies", new[] { "TmdbId", "IsSeries" }, onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddColumn<string>(name: "Embedding", table: "MovieRecommendationFeatures", type: "vector(384)", nullable: true);
        migrationBuilder.Sql("CREATE INDEX \"IX_MovieRecommendationFeatures_Embedding_Hnsw\" ON \"MovieRecommendationFeatures\" USING hnsw (\"Embedding\" vector_cosine_ops)");

        migrationBuilder.CreateTable(
            name: "UserTasteProfiles",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                PositiveProfileJson = table.Column<string>(type: "text", nullable: false),
                NegativeProfileJson = table.Column<string>(type: "text", nullable: false),
                ProfileVersion = table.Column<int>(type: "integer", nullable: false),
                ModelVersion = table.Column<string>(type: "text", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserTasteProfiles", x => x.UserId);
                table.ForeignKey("FK_UserTasteProfiles_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_MovieRecommendationFeatures_Embedding_Hnsw\"");
        migrationBuilder.DropTable(name: "MovieRecommendationFeatures");
        migrationBuilder.DropTable(name: "UserTasteProfiles");
        migrationBuilder.Sql("DROP EXTENSION IF EXISTS vector");
    }
}
