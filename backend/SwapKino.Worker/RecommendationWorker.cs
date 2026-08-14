using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SwapKino.Api;

public sealed class RecommendationWorker(
    IServiceScopeFactory scopes,
    IConnectionMultiplexer redis,
    ILogger<RecommendationWorker> log) : BackgroundService
{
    private const string Stream = "swapkino:events";
    private const string Group = "swapkino-recommendations";
    private readonly string consumer = $"recommendation-{Environment.MachineName}-{Guid.NewGuid():N}";
    private readonly IDatabase database = redis.GetDatabase();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureGroup(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await database.StreamAutoClaimAsync(Stream, Group, consumer, 120_000, "0-0", 20);
                foreach (var entry in claimed.ClaimedEntries) await Process(entry, stoppingToken);
                var entries = await database.StreamReadGroupAsync(Stream, Group, consumer, ">", 20);
                foreach (var entry in entries) await Process(entry, stoppingToken);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP", StringComparison.OrdinalIgnoreCase)) { await EnsureGroup(stoppingToken); }
            catch (Exception ex) { log.LogError(ex, "Recommendation stream cycle failed"); await Task.Delay(2000, stoppingToken); }
            await Task.Delay(500, stoppingToken);
        }
    }

    private async Task Process(StreamEntry entry, CancellationToken ct)
    {
        var topic = Value(entry, "topic");
        if (topic == "recommendations.user.deleted")
        {
            try
            {
                using var document = JsonDocument.Parse(Value(entry, "payload"));
                if (Guid.TryParse(document.RootElement.GetProperty("userId").GetString(), out var deletedUser))
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<RecommendationGateway>().DeleteUserAsync(deletedUser, ct);
                }
                await database.StreamAcknowledgeAsync(Stream, Group, entry.Id);
            }
            catch (Exception ex) { await HandleFailure(entry, ex, ct); }
            return;
        }
        if (topic != "recommendations.action") { await database.StreamAcknowledgeAsync(Stream, Group, entry.Id); return; }
        try
        {
            using var document = JsonDocument.Parse(Value(entry, "payload"));
            if (!document.RootElement.TryGetProperty("userId", out var userNode) || !Guid.TryParse(userNode.GetString(), out var userId))
                throw new InvalidOperationException("recommendations.action has no valid userId");
            var action = document.RootElement.TryGetProperty("action", out var actionNode) ? actionNode.GetString() ?? "" : "";
            var actionId = document.RootElement.TryGetProperty("actionId", out var actionIdNode) && Guid.TryParse(actionIdNode.GetString(), out var parsedActionId) ? parsedActionId : (Guid?)null;
            var tmdbId = document.RootElement.GetProperty("tmdbId").GetInt32();
            var isSeries = document.RootElement.TryGetProperty("isSeries", out var seriesNode) && seriesNode.GetBoolean();
            var value = document.RootElement.TryGetProperty("value", out var valueNode) && valueNode.ValueKind == JsonValueKind.Number ? valueNode.GetDouble() : (double?)null;
            var createdAt = document.RootElement.TryGetProperty("createdAt", out var createdAtNode) && createdAtNode.ValueKind == JsonValueKind.String && createdAtNode.TryGetDateTime(out var eventTime)
                ? eventTime : DateTime.UtcNow;
            using var scope = scopes.CreateScope();
            var gateway = scope.ServiceProvider.GetRequiredService<RecommendationGateway>();
            var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
            var feedback = RecommendationFeedback.Normalize(action, value);
            var movie = await db.Movies.AsNoTracking().SingleOrDefaultAsync(x => x.TmdbId == tmdbId && x.IsSeries == isSeries, ct);
            if (movie is null || !RecommendationEligibility.IsEligible(movie))
            {
                // Keep the action unsynced. HistorySync will reconcile it after
                // catalog enrichment/upsert has made the item eligible.
                await database.StreamAcknowledgeAsync(Stream, Group, entry.Id);
                return;
            }
            var state = await db.UserMovieStates.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.TmdbId == tmdbId && x.IsSeries == isSeries, ct);
            var latest = new UserAction { UserId = userId, TmdbId = tmdbId, IsSeries = isSeries, ActionType = action, Value = value, CreatedAt = createdAt };
            var desired = RecommendationFeedback.Current(state, latest);
            await gateway.ReconcileFeedbackAsync(userId, tmdbId, isSeries, desired, createdAt, ct);
            if (document.RootElement.TryGetProperty("sessionId", out var sessionNode) && sessionNode.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sessionNode.GetString()))
            {
                var sessionKey = $"rec:session:{userId}:{sessionNode.GetString()}";
                await database.HashSetAsync(sessionKey, [
                    new HashEntry("lastFeedback", action),
                    new HashEntry("feedRefreshRequested", feedback?.Type is "negative" or "strong_negative" ? 1 : 0)
                ]);
                if (feedback?.SessionNegative == true)
                {
                    var negativeSet = $"rec:session:{userId}:{sessionNode.GetString()}:negative-items";
                    await database.SetAddAsync(negativeSet, RecommendationGateway.ItemId(tmdbId, isSeries));
                    await database.KeyExpireAsync(negativeSet, TimeSpan.FromHours(12));
                    var intentGenres = await db.MovieGenres.AsNoTracking()
                        .Where(x => x.TmdbId == tmdbId && x.IsSeries == isSeries)
                        .Select(x => x.GenreId).ToListAsync(ct);
                    var intentKey = $"rec:session:{userId}:{sessionNode.GetString()}:intent-genres";
                    foreach (var genreId in intentGenres) await database.SetAddAsync(intentKey, genreId);
                    await database.KeyExpireAsync(intentKey, TimeSpan.FromHours(12));
                }
                await database.KeyExpireAsync(sessionKey, TimeSpan.FromHours(12));
            }
            if (actionId is Guid syncedActionId)
            {
                var storedAction = await db.UserActions.FindAsync([syncedActionId], ct);
                if (storedAction is not null) { storedAction.RecommendationSyncedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            }
            await database.StreamAcknowledgeAsync(Stream, Group, entry.Id);
        }
        catch (Exception ex)
        {
            await HandleFailure(entry, ex, ct);
        }
    }

    private async Task HandleFailure(StreamEntry entry, Exception ex, CancellationToken ct)
    {
        var pending = await database.StreamPendingMessagesAsync(Stream, Group, 1, consumer, entry.Id, entry.Id);
        var deliveries = pending.Length == 0 ? 1 : pending[0].DeliveryCount;
        if (deliveries >= 5)
        {
            await database.StreamAddAsync("swapkino:events:dead-letter", [
                new NameValueEntry("event_id", Value(entry, "event_id")),
                new NameValueEntry("topic", Value(entry, "topic")),
                new NameValueEntry("payload", Value(entry, "payload")),
                new NameValueEntry("error", ex.Message),
                new NameValueEntry("attempts", deliveries)]);
            await database.StreamAcknowledgeAsync(Stream, Group, entry.Id);
            log.LogError(ex, "Recommendation event {EventId} moved to dead-letter after {Attempts} attempts", entry.Id, deliveries);
        }
        else log.LogError(ex, "Recommendation event {EventId} failed on delivery {Attempt}; it will be reclaimed", entry.Id, deliveries);
        await Task.CompletedTask;
    }

    private async Task EnsureGroup(CancellationToken ct)
    {
        try { await database.StreamCreateConsumerGroupAsync(Stream, Group, "0-0", createStream: true); }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { }
        await Task.CompletedTask;
    }

    private static string Value(StreamEntry entry, string name) => entry.Values.FirstOrDefault(x => x.Name == name).Value.ToString();
}
