using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("202608070005_NormalizedCatalog")]
public partial class NormalizedCatalog : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.Sql("""
        ALTER TABLE "UserActions" DROP CONSTRAINT IF EXISTS "FK_UserActions_Movies_TmdbId";
        ALTER TABLE "Movies" DROP CONSTRAINT IF EXISTS "PK_Movies";
        ALTER TABLE "Movies" ADD COLUMN IF NOT EXISTS "IsSeries" boolean NOT NULL DEFAULT false;
        ALTER TABLE "Movies" ADD COLUMN IF NOT EXISTS "Tagline" text NULL;
        ALTER TABLE "Movies" ADD COLUMN IF NOT EXISTS "OriginalLanguage" text NULL;
        ALTER TABLE "Movies" ADD COLUMN IF NOT EXISTS "SummaryUpdatedAt" timestamp with time zone NOT NULL DEFAULT now();
        ALTER TABLE "Movies" ADD COLUMN IF NOT EXISTS "DetailsUpdatedAt" timestamp with time zone NULL;
        ALTER TABLE "Movies" ADD COLUMN IF NOT EXISTS "DetailAttemptCount" integer NOT NULL DEFAULT 0;
        ALTER TABLE "Movies" ADD CONSTRAINT "PK_Movies" PRIMARY KEY ("TmdbId", "IsSeries");
        ALTER TABLE "UserActions" ADD COLUMN IF NOT EXISTS "IsSeries" boolean NOT NULL DEFAULT false;
        ALTER TABLE "UserActions" ADD CONSTRAINT "FK_UserActions_Movies_TmdbId_IsSeries" FOREIGN KEY ("TmdbId", "IsSeries") REFERENCES "Movies" ("TmdbId", "IsSeries") ON DELETE CASCADE;
        DROP INDEX IF EXISTS "IX_UserActions_UserId_TmdbId";
        CREATE INDEX IF NOT EXISTS "IX_UserActions_UserId_TmdbId_IsSeries" ON "UserActions" ("UserId", "TmdbId", "IsSeries");
        CREATE INDEX IF NOT EXISTS "IX_Movies_IsSeries_Popularity_TmdbId" ON "Movies" ("IsSeries", "Popularity", "TmdbId");

        CREATE TABLE "Genres" ("TmdbId" integer PRIMARY KEY, "Slug" text NOT NULL, "Name" text NOT NULL, "IsSeries" boolean NOT NULL DEFAULT false);
        CREATE UNIQUE INDEX "IX_Genres_Slug" ON "Genres" ("Slug");
        CREATE TABLE "MovieGenres" ("TmdbId" integer NOT NULL, "IsSeries" boolean NOT NULL DEFAULT false, "GenreId" integer NOT NULL,
          CONSTRAINT "PK_MovieGenres" PRIMARY KEY ("TmdbId", "IsSeries", "GenreId"),
          CONSTRAINT "FK_MovieGenres_Movies_TmdbId_IsSeries" FOREIGN KEY ("TmdbId", "IsSeries") REFERENCES "Movies" ("TmdbId", "IsSeries") ON DELETE CASCADE,
          CONSTRAINT "FK_MovieGenres_Genres_GenreId" FOREIGN KEY ("GenreId") REFERENCES "Genres" ("TmdbId") ON DELETE CASCADE);
        CREATE INDEX "IX_MovieGenres_GenreId" ON "MovieGenres" ("GenreId");

        CREATE TABLE "UserMovieStates" ("UserId" uuid NOT NULL, "TmdbId" integer NOT NULL, "IsSeries" boolean NOT NULL DEFAULT false, "Rating" double precision NULL, "Favorite" boolean NOT NULL DEFAULT false, "Watched" boolean NOT NULL DEFAULT false, "SuppressedUntil" timestamp with time zone NULL, "PositiveSignals" integer NOT NULL DEFAULT 0, "NegativeSignals" integer NOT NULL DEFAULT 0, "LastImpressionAt" timestamp with time zone NULL, "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(), CONSTRAINT "PK_UserMovieStates" PRIMARY KEY ("UserId", "TmdbId", "IsSeries"), CONSTRAINT "FK_UserMovieStates_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE);

        ALTER TABLE "ImportItems" ADD COLUMN IF NOT EXISTS "ExternalId" text NOT NULL DEFAULT '';
        ALTER TABLE "ImportItems" ADD COLUMN IF NOT EXISTS "IsSeries" boolean NOT NULL DEFAULT false;
        UPDATE "ImportItems" SET "ExternalId" = CASE WHEN "KinopoiskUrl" ~ '/(film|series)/[0-9]+' THEN regexp_replace("KinopoiskUrl", '^.*/(film|series)/([0-9]+).*$','\2') ELSE "KinopoiskUrl" END WHERE "ExternalId" = '';
        -- Старый импорт был уникален по полному URL, поэтому URL-варианты одной
        -- карточки могли существовать внутри job. После перехода на стабильный
        -- Kinopoisk ID оставляем самую свежую staging-запись; применённые actions
        -- и пользовательская библиотека этой очисткой не затрагиваются.
        WITH ranked AS (
          SELECT "Id", row_number() OVER (
            PARTITION BY "ImportJobId", "ExternalId"
            ORDER BY "CreatedAt" DESC, "Id" DESC
          ) AS rn
          FROM "ImportItems"
        )
        DELETE FROM "ImportItems" item USING ranked
        WHERE item."Id" = ranked."Id" AND ranked.rn > 1;
        DROP INDEX IF EXISTS "IX_ImportItems_ImportJobId_KinopoiskUrl";
        CREATE UNIQUE INDEX "IX_ImportItems_ImportJobId_ExternalId" ON "ImportItems" ("ImportJobId", "ExternalId");
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "Phase" text NOT NULL DEFAULT 'Queued';
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "PhaseProgress" integer NOT NULL DEFAULT 0;
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "DiscoveredCount" integer NOT NULL DEFAULT 0;
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "MatchedCount" integer NOT NULL DEFAULT 0;
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "AppliedCount" integer NOT NULL DEFAULT 0;
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "UnmatchedCount" integer NOT NULL DEFAULT 0;
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "PagesProcessed" integer NOT NULL DEFAULT 0;
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "PagesTotal" integer NULL;
        ALTER TABLE "ImportJobs" ADD COLUMN IF NOT EXISTS "EstimatedRemainingSeconds" integer NULL;
        DROP INDEX IF EXISTS "IX_ImportJobs_Active_User_Profile";
        CREATE UNIQUE INDEX "IX_ImportJobs_Active_User_Profile" ON "ImportJobs" ("UserId", "ProfileUrl") WHERE "Status" IN ('Queued','Running','Scraping','Matching','Applying','WaitingForUser');

        CREATE TABLE "UserExternalItems" ("UserId" uuid NOT NULL, "Source" text NOT NULL, "ProfileId" text NOT NULL, "ExternalId" text NOT NULL, "TmdbId" integer NULL, "IsSeries" boolean NOT NULL DEFAULT false, "Rating" double precision NULL, "Watched" boolean NOT NULL DEFAULT false, "MatchStatus" text NOT NULL DEFAULT 'pending', "MatchError" text NULL, "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(), CONSTRAINT "PK_UserExternalItems" PRIMARY KEY ("UserId", "Source", "ProfileId", "ExternalId"), CONSTRAINT "FK_UserExternalItems_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE);

        -- Idempotent normalization of both TMDB summary genre_ids and detail genres.
        INSERT INTO "Genres" ("TmdbId","Slug","Name","IsSeries")
        SELECT DISTINCT gid, gid::text, gid::text, false FROM (
          SELECT (jsonb_array_elements_text(("Payload"::jsonb)->'genre_ids'))::int gid FROM "Movies" WHERE jsonb_typeof(("Payload"::jsonb)->'genre_ids')='array'
          UNION ALL SELECT (g->>'id')::int FROM "Movies", LATERAL jsonb_array_elements(("Payload"::jsonb)->'genres') g WHERE jsonb_typeof(("Payload"::jsonb)->'genres')='array'
        ) ids ON CONFLICT ("TmdbId") DO NOTHING;
        INSERT INTO "MovieGenres" ("TmdbId","IsSeries","GenreId")
        SELECT DISTINCT "TmdbId","IsSeries",gid FROM (
          SELECT "TmdbId","IsSeries",(jsonb_array_elements_text(("Payload"::jsonb)->'genre_ids'))::int gid FROM "Movies" WHERE jsonb_typeof(("Payload"::jsonb)->'genre_ids')='array'
          UNION ALL SELECT "TmdbId","IsSeries",(g->>'id')::int FROM "Movies", LATERAL jsonb_array_elements(("Payload"::jsonb)->'genres') g WHERE jsonb_typeof(("Payload"::jsonb)->'genres')='array'
        ) links ON CONFLICT DO NOTHING;
        UPDATE "Movies" SET "DetailsState"='summary' WHERE "DetailsState"='ready' AND ("RuntimeMinutes" IS NULL OR "Payload"::jsonb ? 'genre_ids');
        """);
    }

    protected override void Down(MigrationBuilder m) => throw new NotSupportedException("The normalized catalog migration is intentionally data-preserving and cannot be automatically reversed.");
}
