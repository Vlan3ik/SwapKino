using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("202608090001_ProfileLibraryIndexes")]
public partial class ProfileLibraryIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_UserMovieStates_UserId_Favorite_UpdatedAt\" ON \"UserMovieStates\" (\"UserId\", \"Favorite\", \"UpdatedAt\" DESC);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_UserMovieStates_UserId_Rating_UpdatedAt\" ON \"UserMovieStates\" (\"UserId\", \"Rating\", \"UpdatedAt\" DESC);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_UserMovieStates_UserId_TmdbId_IsSeries\" ON \"UserMovieStates\" (\"UserId\", \"TmdbId\", \"IsSeries\");");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_UserMovieStates_UserId_Favorite_UpdatedAt\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_UserMovieStates_UserId_Rating_UpdatedAt\";");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_UserMovieStates_UserId_TmdbId_IsSeries\";");
    }
}
