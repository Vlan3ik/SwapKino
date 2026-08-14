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
                var actions = await db.UserActions.Where(x => x.RecommendationSyncedAt == null).OrderBy(x => x.CreatedAt).Take(250).ToListAsync(ct);
                if (actions.Count > 0)
                {
                    var feedback = actions.Select(x => (Action: x, Normalized: RecommendationFeedback.Normalize(x.ActionType, x.Value)))
                        .Where(x => x.Normalized is not null)
                        .Select(x => new GorseFeedback(x.Normalized!.Type, x.Action.UserId.ToString(), RecommendationGateway.ItemId(x.Action.TmdbId, x.Action.IsSeries), x.Normalized.Value, x.Action.CreatedAt))
                        .ToArray();
                    if (feedback.Length > 0) await gateway.SendFeedbackAsync(feedback, ct);
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
