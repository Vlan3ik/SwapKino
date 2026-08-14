using Microsoft.EntityFrameworkCore;
using SwapKino.Api;

public sealed class RecommendationCatalogWorker(IServiceScopeFactory scopes, ILogger<RecommendationCatalogWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
                var gateway = scope.ServiceProvider.GetRequiredService<RecommendationGateway>();
                var movies = await db.Movies.AsNoTracking()
                    .Include(x => x.MovieGenres).Include(x => x.MovieKeywords).Include(x => x.MoviePeople)
                    .Where(x => x.DetailsState == "ready" && (x.PosterPath != null || x.BackdropPath != null) && (x.RecommendationSyncedAt == null || x.RecommendationSyncedAt < x.UpdatedAt))
                    .OrderBy(x => x.UpdatedAt).Take(25).ToListAsync(ct);
                foreach (var movie in movies)
                {
                    var memberships = ThemeRegistry.Classify(movie);
                    var existing = await db.MovieThemeMemberships.Where(x => x.TmdbId == movie.TmdbId && x.IsSeries == movie.IsSeries).ToListAsync(ct);
                    db.MovieThemeMemberships.RemoveRange(existing);
                    db.MovieThemeMemberships.AddRange(memberships.Select(x => new MovieThemeMembership { TmdbId = movie.TmdbId, IsSeries = movie.IsSeries, ThemeSlug = x.Slug, Confidence = x.Confidence, ThemeVersion = ThemeRegistry.Version }));
                    await gateway.UpsertItemAsync(movie, memberships, ct);
                    var tracked = await db.Movies.SingleAsync(x => x.TmdbId == movie.TmdbId && x.IsSeries == movie.IsSeries, ct);
                    tracked.RecommendationSyncedAt = DateTime.UtcNow;
                }
                if (movies.Count > 0) await db.SaveChangesAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Recommendation catalog sync failed");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
    }
}
