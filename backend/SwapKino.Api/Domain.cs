using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

public sealed class User : IdentityUser<Guid> { public string? DisplayName { get; set; } public string? AvatarUrl { get; set; } public string? KinopoiskProfileUrl { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; public DateTime? PrivacyConsentAt { get; set; } public string? PrivacyConsentVersion { get; set; } }

public sealed class Movie
{
    public int TmdbId { get; set; }
    public bool IsSeries { get; set; }
    public int? KinopoiskId { get; set; }
    public string? ImdbId { get; set; }
    public string Title { get; set; } = "";
    public string? OriginalTitle { get; set; }
    public string? Tagline { get; set; }
    public string? Overview { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? ReleaseDate { get; set; }
    public int? RuntimeMinutes { get; set; }
    public double VoteAverage { get; set; }
    public int VoteCount { get; set; }
    public double Popularity { get; set; }
    public bool Adult { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string DetailsState { get; set; } = "summary";
    public int DetailAttemptCount { get; set; }
    public string Payload { get; set; } = "{}";
    public DateTime SummaryUpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DetailsUpdatedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<MovieGenre> MovieGenres { get; set; } = [];
    public ICollection<MovieKeyword> MovieKeywords { get; set; } = [];
    public ICollection<MoviePerson> MoviePeople { get; set; } = [];
}
public sealed class Genre { public int TmdbId { get; set; } public string Slug { get; set; } = ""; public string Name { get; set; } = ""; public bool IsSeries { get; set; } public ICollection<MovieGenre> MovieGenres { get; set; } = []; }
public sealed class MovieGenre { public int TmdbId { get; set; } public bool IsSeries { get; set; } public int GenreId { get; set; } public Movie Movie { get; set; } = null!; public Genre Genre { get; set; } = null!; }
public sealed class Keyword { public int TmdbId { get; set; } public string Name { get; set; } = ""; public string Slug { get; set; } = ""; public ICollection<MovieKeyword> MovieKeywords { get; set; } = []; }
public sealed class MovieKeyword { public int TmdbId { get; set; } public bool IsSeries { get; set; } public int KeywordId { get; set; } public Movie Movie { get; set; } = null!; public Keyword Keyword { get; set; } = null!; }
public sealed class MoviePerson { public int TmdbId { get; set; } public bool IsSeries { get; set; } public int PersonId { get; set; } public string Name { get; set; } = ""; public string Department { get; set; } = ""; public string? Character { get; set; } public int SortOrder { get; set; } public Movie Movie { get; set; } = null!; }
public sealed class RecommendationImpression { public Guid Id { get; set; } = Guid.NewGuid(); public Guid? UserId { get; set; } public int TmdbId { get; set; } public bool IsSeries { get; set; } public string ThemeId { get; set; } = ""; public int Position { get; set; } public string Reason { get; set; } = ""; public string FeedItemType { get; set; } = "movie"; public string RankerVersion { get; set; } = RecommendationRanking.RankerVersion; public int ProfileVersion { get; set; } public string? SessionId { get; set; } public DateTime ShownAt { get; set; } = DateTime.UtcNow; }

public sealed class MovieRecommendationFeature
{
    public int TmdbId { get; set; }
    public bool IsSeries { get; set; }
    public string FeatureJson { get; set; } = "{}";
    public string FeatureVersion { get; set; } = "v1";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class UserTasteProfile
{
    public Guid UserId { get; set; }
    public string PositiveProfileJson { get; set; } = "{}";
    public string NegativeProfileJson { get; set; } = "{}";
    public string PositiveEmbeddingJson { get; set; } = "[]";
    public string NegativeEmbeddingJson { get; set; } = "[]";
    public int ProfileVersion { get; set; }
    public string ModelVersion { get; set; } = RecommendationProfileBuilder.ModelVersion;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class UserAction { public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public int TmdbId { get; set; } public bool IsSeries { get; set; } public string ActionType { get; set; } = ""; public double? Value { get; set; } public string IdempotencyKey { get; set; } = ""; public string? SessionId { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public sealed class UserMovieState
{
    public Guid UserId { get; set; }
    public int TmdbId { get; set; }
    public bool IsSeries { get; set; }
    public double? Rating { get; set; }
    public bool Favorite { get; set; }
    public bool Watched { get; set; }
    public DateTime? SuppressedUntil { get; set; }
    public int PositiveSignals { get; set; }
    public int NegativeSignals { get; set; }
    public DateTime? LastImpressionAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class RefreshSession { public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public string TokenHash { get; set; } = ""; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; public DateTime ExpiresAt { get; set; } public DateTime? RevokedAt { get; set; } }
public sealed class ImportJob { public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public string ProfileUrl { get; set; } = ""; public string Status { get; set; } = "Queued"; public string Phase { get; set; } = "Queued"; public int Progress { get; set; } public int PhaseProgress { get; set; } public int ImportedCount { get; set; } public int DiscoveredCount { get; set; } public int MatchedCount { get; set; } public int AppliedCount { get; set; } public int UnmatchedCount { get; set; } public int PagesProcessed { get; set; } public int? PagesTotal { get; set; } public int? EstimatedRemainingSeconds { get; set; } public string? Error { get; set; } public string Checkpoint { get; set; } = "{}"; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; }
public sealed class ImportItem { public Guid Id { get; set; } = Guid.NewGuid(); public Guid ImportJobId { get; set; } public string ExternalId { get; set; } = ""; public string KinopoiskUrl { get; set; } = ""; public string Title { get; set; } = ""; public int? Year { get; set; } public string? Genres { get; set; } public double? Rating { get; set; } public string Kind { get; set; } = "film"; public bool IsSeries { get; set; } public int Page { get; set; } public string MatchStatus { get; set; } = "pending"; public int? TmdbId { get; set; } public string? MatchError { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public sealed class UserExternalItem { public Guid UserId { get; set; } public string Source { get; set; } = "kinopoisk"; public string ProfileId { get; set; } = ""; public string ExternalId { get; set; } = ""; public int? TmdbId { get; set; } public bool IsSeries { get; set; } public double? Rating { get; set; } public bool Watched { get; set; } public string MatchStatus { get; set; } = "pending"; public string? MatchError { get; set; } public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; }
public sealed class OutboxEvent { public Guid Id { get; set; } = Guid.NewGuid(); public string Topic { get; set; } = ""; public string Payload { get; set; } = "{}"; public bool Published { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; public DateTime? PublishedAt { get; set; } public int AttemptCount { get; set; } public DateTime? NextAttemptAt { get; set; } public string? LastError { get; set; } public string? LockedBy { get; set; } public DateTime? LockedUntil { get; set; } }
public sealed class CatalogSyncState { public string Source { get; set; } = ""; public bool IsSeries { get; set; } public int NextPage { get; set; } = 1; public int? TotalPages { get; set; } public long ImportedCount { get; set; } public DateTime? LastFetchedAt { get; set; } public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; }

public sealed class SwapKinoDbContext(DbContextOptions<SwapKinoDbContext> options) : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Movie> Movies => Set<Movie>(); public DbSet<Genre> Genres => Set<Genre>(); public DbSet<MovieGenre> MovieGenres => Set<MovieGenre>(); public DbSet<Keyword> Keywords => Set<Keyword>(); public DbSet<MovieKeyword> MovieKeywords => Set<MovieKeyword>(); public DbSet<MoviePerson> MoviePeople => Set<MoviePerson>(); public DbSet<UserAction> UserActions => Set<UserAction>(); public DbSet<UserMovieState> UserMovieStates => Set<UserMovieState>(); public DbSet<UserExternalItem> UserExternalItems => Set<UserExternalItem>(); public DbSet<RecommendationImpression> RecommendationImpressions => Set<RecommendationImpression>(); public DbSet<MovieRecommendationFeature> MovieRecommendationFeatures => Set<MovieRecommendationFeature>(); public DbSet<UserTasteProfile> UserTasteProfiles => Set<UserTasteProfile>(); public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>(); public DbSet<ImportJob> ImportJobs => Set<ImportJob>(); public DbSet<ImportItem> ImportItems => Set<ImportItem>(); public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>(); public DbSet<CatalogSyncState> CatalogSyncStates => Set<CatalogSyncState>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Movie>().HasKey(x => new { x.TmdbId, x.IsSeries });
        b.Entity<Movie>().HasIndex(x => new { x.IsSeries, x.Popularity, x.TmdbId });
        b.Entity<Movie>().HasIndex(x => new { x.KinopoiskId, x.IsSeries });
        b.Entity<Movie>().HasIndex(x => x.ImdbId);
        b.Entity<Genre>().HasKey(x => x.TmdbId);
        b.Entity<Genre>().HasIndex(x => x.Slug).IsUnique();
        b.Entity<MovieGenre>().HasKey(x => new { x.TmdbId, x.IsSeries, x.GenreId });
        b.Entity<MovieGenre>().HasOne(x => x.Movie).WithMany(x => x.MovieGenres).HasForeignKey(x => new { x.TmdbId, x.IsSeries }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MovieGenre>().HasOne(x => x.Genre).WithMany(x => x.MovieGenres).HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Keyword>().HasKey(x => x.TmdbId); b.Entity<Keyword>().HasIndex(x => x.Slug);
        b.Entity<MovieKeyword>().HasKey(x => new { x.TmdbId, x.IsSeries, x.KeywordId });
        b.Entity<MovieKeyword>().HasOne(x => x.Movie).WithMany(x => x.MovieKeywords).HasForeignKey(x => new { x.TmdbId, x.IsSeries }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MovieKeyword>().HasOne(x => x.Keyword).WithMany(x => x.MovieKeywords).HasForeignKey(x => x.KeywordId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MoviePerson>().HasKey(x => new { x.TmdbId, x.IsSeries, x.PersonId, x.Department });
        b.Entity<MoviePerson>().HasOne(x => x.Movie).WithMany(x => x.MoviePeople).HasForeignKey(x => new { x.TmdbId, x.IsSeries }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserAction>().HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique(); b.Entity<UserAction>().HasIndex(x => new { x.UserId, x.TmdbId, x.IsSeries }); b.Entity<UserAction>().HasIndex(x => new { x.UserId, x.SessionId, x.CreatedAt });
        b.Entity<UserMovieState>().HasKey(x => new { x.UserId, x.TmdbId, x.IsSeries });
        b.Entity<UserExternalItem>().HasKey(x => new { x.UserId, x.Source, x.ProfileId, x.ExternalId });
        b.Entity<RecommendationImpression>().HasIndex(x => new { x.UserId, x.TmdbId, x.IsSeries, x.ShownAt });
        b.Entity<MovieRecommendationFeature>().HasKey(x => new { x.TmdbId, x.IsSeries });
        b.Entity<MovieRecommendationFeature>().HasOne<Movie>().WithMany().HasForeignKey(x => new { x.TmdbId, x.IsSeries }).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserTasteProfile>().HasKey(x => x.UserId);
        b.Entity<UserTasteProfile>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RefreshSession>().HasIndex(x => x.TokenHash).IsUnique(); b.Entity<RefreshSession>().HasIndex(x => new { x.UserId, x.ExpiresAt });
        b.Entity<ImportJob>().HasIndex(x => new { x.UserId, x.CreatedAt }); b.Entity<ImportJob>().HasIndex(x => new { x.UserId, x.ProfileUrl }).IsUnique().HasFilter("\"Status\" IN ('Queued', 'Scraping', 'Matching', 'Applying', 'Running', 'WaitingForUser')");
        b.Entity<ImportItem>().HasIndex(x => new { x.ImportJobId, x.ExternalId }).IsUnique(); b.Entity<ImportItem>().HasIndex(x => new { x.ImportJobId, x.MatchStatus }); b.Entity<ImportItem>().HasOne<ImportJob>().WithMany().HasForeignKey(x => x.ImportJobId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RefreshSession>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade); b.Entity<OutboxEvent>().HasIndex(x => x.Published);
        b.Entity<CatalogSyncState>().HasKey(x => new { x.Source, x.IsSeries });
    }
}

public sealed record GenreDto(int Id, string Slug, string Name);
public record MovieSummaryDto(int Id, int TmdbId, bool IsSeries, string Title, string? OriginalTitle, string? Tagline, string? Overview, string? ReleaseDate, int? Runtime, double? Rating, int VoteCount, IReadOnlyList<GenreDto> Genres, string? PosterUrl, string? BackdropUrl, string DetailsState);
public sealed record MovieDetailsDto(int Id, int TmdbId, bool IsSeries, string Title, string? OriginalTitle, string? Tagline, string? Overview, string? ReleaseDate, int? Runtime, double? Rating, int VoteCount, IReadOnlyList<GenreDto> Genres, string? PosterUrl, string? BackdropUrl, string DetailsState, object[] Cast, object[] Crew, object[] Trailers, object[] Images, object? WatchProviders)
    : MovieSummaryDto(Id, TmdbId, IsSeries, Title, OriginalTitle, Tagline, Overview, ReleaseDate, Runtime, Rating, VoteCount, Genres, PosterUrl, BackdropUrl, DetailsState);

public sealed record LibraryCursor(DateTime UpdatedAt, double? UserRating, string? Text, int TmdbId, bool IsSeries);

public static class MovieDto
{
    private static string? Image(string? path, string size) => string.IsNullOrWhiteSpace(path) ? null : $"https://image.tmdb.org/t/p/{size}{path}";
    public static MovieSummaryDto Summary(Movie m) => new(m.TmdbId, m.TmdbId, m.IsSeries, m.Title, m.OriginalTitle, m.Tagline, m.Overview, m.ReleaseDate, m.VoteCount > 0 ? m.RuntimeMinutes : m.RuntimeMinutes, m.VoteCount > 0 ? m.VoteAverage : null, m.VoteCount, m.MovieGenres.Select(x => new GenreDto(x.Genre.TmdbId, x.Genre.Slug, x.Genre.Name)).OrderBy(x => x.Name).ToArray(), Image(m.PosterPath, "w500"), Image(m.BackdropPath, "original"), m.DetailsState);
    public static MovieDetailsDto Details(Movie m)
    {
        JsonElement root = default; try { root = JsonDocument.Parse(m.Payload).RootElement.Clone(); } catch (JsonException) { }
        object[] Array(string parent, string? child = null)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(parent, out var node)) return [];
            if (child is not null && (!node.TryGetProperty(child, out node))) return [];
            return node.ValueKind == JsonValueKind.Array ? node.EnumerateArray().Select(x => (object)x.Clone()).ToArray() : [];
        }
        var s = Summary(m);
        object? providers = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("watch/providers", out var wp) ? wp.Clone() : null;
        return new(s.Id,s.TmdbId,s.IsSeries,s.Title,s.OriginalTitle,s.Tagline,s.Overview,s.ReleaseDate,s.Runtime,s.Rating,s.VoteCount,s.Genres,s.PosterUrl,s.BackdropUrl,s.DetailsState,Array("credits","cast"),Array("credits","crew"),Array("videos","results"),Array("images","backdrops"),providers);
    }
    public static object From(Movie m) => Summary(m);
}
