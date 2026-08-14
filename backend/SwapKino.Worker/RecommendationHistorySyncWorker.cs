using Microsoft.EntityFrameworkCore;
using SwapKino.Api;

public sealed class RecommendationHistorySyncWorker(IServiceScopeFactory scopes, ILogger<RecommendationHistorySyncWorker> log) : BackgroundService
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
                var eligibleMovies = RecommendationEligibility.Apply(db.Movies.AsNoTracking())
                    .Select(x => new { x.TmdbId, x.IsSeries });
                var actions = await db.UserActions
                    .Where(x => x.RecommendationSyncedAt == null)
                    .Join(eligibleMovies,
                        action => new { action.TmdbId, action.IsSeries }, movie => new { movie.TmdbId, movie.IsSeries }, (action, movie) => action)
                    .OrderBy(x => x.CreatedAt).Take(250).ToListAsync(ct);
                if (actions.Count > 0)
                {
                    foreach (var group in actions.GroupBy(x => new { x.UserId, x.TmdbId, x.IsSeries }))
                    {
                        var state = await db.UserMovieStates.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == group.Key.UserId && x.TmdbId == group.Key.TmdbId && x.IsSeries == group.Key.IsSeries, ct);
                        var latest = group.OrderByDescending(x => x.CreatedAt).First();
                        await gateway.ReconcileFeedbackAsync(group.Key.UserId, group.Key.TmdbId, group.Key.IsSeries,
                            RecommendationFeedback.Current(state, latest), latest.CreatedAt, ct);
                    }
                    foreach (var action in actions) action.RecommendationSyncedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
                await Task.Delay(actions.Count == 0 ? TimeSpan.FromSeconds(15) : TimeSpan.FromMilliseconds(100), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Recommendation history sync failed; will retry");
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
        }
    }
}
