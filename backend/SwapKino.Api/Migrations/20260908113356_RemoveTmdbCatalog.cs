using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SwapKino.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTmdbCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogSyncStates");

            migrationBuilder.DropTable(
                name: "MovieGenres");

            migrationBuilder.DropTable(
                name: "MovieKeywords");

            migrationBuilder.DropTable(
                name: "MoviePeople");

            migrationBuilder.DropTable(
                name: "MovieThemeMemberships");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Keywords");

            migrationBuilder.DropTable(
                name: "Movies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogSyncStates",
                columns: table => new
                {
                    Source = table.Column<string>(type: "text", nullable: false),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    ImportedCount = table.Column<long>(type: "bigint", nullable: false),
                    LastFetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextPage = table.Column<int>(type: "integer", nullable: false),
                    TotalPages = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogSyncStates", x => new { x.Source, x.IsSeries });
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.TmdbId);
                });

            migrationBuilder.CreateTable(
                name: "Keywords",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Keywords", x => x.TmdbId);
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "integer", nullable: false),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    Adult = table.Column<bool>(type: "boolean", nullable: false),
                    BackdropPath = table.Column<string>(type: "text", nullable: true),
                    DetailAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    DetailsState = table.Column<string>(type: "text", nullable: false),
                    DetailsUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ImdbId = table.Column<string>(type: "text", nullable: true),
                    KinopoiskId = table.Column<int>(type: "integer", nullable: true),
                    OriginalLanguage = table.Column<string>(type: "text", nullable: true),
                    OriginalTitle = table.Column<string>(type: "text", nullable: true),
                    Overview = table.Column<string>(type: "text", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Popularity = table.Column<double>(type: "double precision", nullable: false),
                    PosterPath = table.Column<string>(type: "text", nullable: true),
                    RecommendationSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecommendationThemeVersion = table.Column<int>(type: "integer", nullable: true),
                    ReleaseDate = table.Column<string>(type: "text", nullable: true),
                    RuntimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    SummaryUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tagline = table.Column<string>(type: "text", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VoteAverage = table.Column<double>(type: "double precision", nullable: false),
                    VoteCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => new { x.TmdbId, x.IsSeries });
                });

            migrationBuilder.CreateTable(
                name: "MovieGenres",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "integer", nullable: false),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    GenreId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieGenres", x => new { x.TmdbId, x.IsSeries, x.GenreId });
                    table.ForeignKey(
                        name: "FK_MovieGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "TmdbId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MovieGenres_Movies_TmdbId_IsSeries",
                        columns: x => new { x.TmdbId, x.IsSeries },
                        principalTable: "Movies",
                        principalColumns: new[] { "TmdbId", "IsSeries" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovieKeywords",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "integer", nullable: false),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    KeywordId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieKeywords", x => new { x.TmdbId, x.IsSeries, x.KeywordId });
                    table.ForeignKey(
                        name: "FK_MovieKeywords_Keywords_KeywordId",
                        column: x => x.KeywordId,
                        principalTable: "Keywords",
                        principalColumn: "TmdbId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MovieKeywords_Movies_TmdbId_IsSeries",
                        columns: x => new { x.TmdbId, x.IsSeries },
                        principalTable: "Movies",
                        principalColumns: new[] { "TmdbId", "IsSeries" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MoviePeople",
                columns: table => new
                {
                    TmdbId = table.Column<int>(type: "integer", nullable: false),
                    IsSeries = table.Column<bool>(type: "boolean", nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: false),
                    Department = table.Column<string>(type: "text", nullable: false),
                    Character = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoviePeople", x => new { x.TmdbId, x.IsSeries, x.PersonId, x.Department });
                    table.ForeignKey(
                        name: "FK_MoviePeople_Movies_TmdbId_IsSeries",
                        columns: x => new { x.TmdbId, x.IsSeries },
                        principalTable: "Movies",
                        principalColumns: new[] { "TmdbId", "IsSeries" },
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Slug",
                table: "Genres",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Keywords_Slug",
                table: "Keywords",
                column: "Slug");

            migrationBuilder.CreateIndex(
                name: "IX_MovieGenres_GenreId",
                table: "MovieGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_MovieKeywords_KeywordId",
                table: "MovieKeywords",
                column: "KeywordId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_ImdbId",
                table: "Movies",
                column: "ImdbId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_IsSeries_Popularity_TmdbId",
                table: "Movies",
                columns: new[] { "IsSeries", "Popularity", "TmdbId" });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_KinopoiskId_IsSeries",
                table: "Movies",
                columns: new[] { "KinopoiskId", "IsSeries" });
        }
    }
}
