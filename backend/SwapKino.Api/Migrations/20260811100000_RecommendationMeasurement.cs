using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SwapKino.Api;

#nullable disable

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("20260811100000_RecommendationMeasurement")]
public partial class RecommendationMeasurement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FeedItemType", table: "RecommendationImpressions", type: "text", nullable: false, defaultValue: "movie");
        migrationBuilder.AddColumn<string>(name: "RankerVersion", table: "RecommendationImpressions", type: "text", nullable: false, defaultValue: "ranker-v2-ann-mmr");
        migrationBuilder.AddColumn<int>(name: "ProfileVersion", table: "RecommendationImpressions", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "SessionId", table: "RecommendationImpressions", type: "text", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FeedItemType", table: "RecommendationImpressions");
        migrationBuilder.DropColumn(name: "RankerVersion", table: "RecommendationImpressions");
        migrationBuilder.DropColumn(name: "ProfileVersion", table: "RecommendationImpressions");
        migrationBuilder.DropColumn(name: "SessionId", table: "RecommendationImpressions");
    }
}
