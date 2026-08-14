using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapKino.Api.Migrations;

[Migration("20260814133000_RemoveLegacyRecommendationTables")]
[DbContext(typeof(SwapKinoDbContext))]
public partial class RemoveLegacyRecommendationTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_MovieRecommendationFeatures_Embedding_Hnsw\"");
        migrationBuilder.DropTable(name: "MovieRecommendationFeatures");
        migrationBuilder.DropTable(name: "UserTasteProfiles");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Legacy recommendation state is intentionally not recreated. Gorse and
        // the event history are the source of the rebuilt recommendation state.
    }
}
