using System.Text.Json;
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
        if (topic != "recommendations.action") { await database.StreamAcknowledgeAsync(Stream, Group, entry.Id); return; }
        try
        {
            using var document = JsonDocument.Parse(Value(entry, "payload"));
            if (!document.RootElement.TryGetProperty("userId", out var userNode) || !Guid.TryParse(userNode.GetString(), out var userId))
                throw new InvalidOperationException("recommendations.action has no valid userId");
            var action = document.RootElement.TryGetProperty("action", out var actionNode) ? actionNode.GetString() ?? "" : "";
            var tmdbId = document.RootElement.GetProperty("tmdbId").GetInt32();
            var isSeries = document.RootElement.TryGetProperty("isSeries", out var seriesNode) && seriesNode.GetBoolean();
            var value = document.RootElement.TryGetProperty("value", out var valueNode) && valueNode.ValueKind == JsonValueKind.Number ? valueNode.GetDouble() : (double?)null;
            using var scope = scopes.CreateScope();
            var gateway = scope.ServiceProvider.GetRequiredService<RecommendationGateway>();
            var feedback = Normalize(action, value);
            if (feedback is not null)
                await gateway.SendFeedbackAsync([new GorseFeedback(feedback, userId.ToString(), RecommendationGateway.ItemId(tmdbId, isSeries), 1, DateTime.UtcNow)], ct);
            if (document.RootElement.TryGetProperty("sessionId", out var sessionNode) && sessionNode.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sessionNode.GetString()))
            {
                var sessionKey = $"rec:session:{userId}:{sessionNode.GetString()}";
                await database.HashSetAsync(sessionKey, [
                    new HashEntry("lastFeedback", action),
                    new HashEntry("feedRefreshRequested", feedback is "negative" ? 1 : 0)
                ]);
                await database.KeyExpireAsync(sessionKey, TimeSpan.FromHours(12));
            }
            await database.StreamAcknowledgeAsync(Stream, Group, entry.Id);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Recommendation event {EventId} failed", entry.Id);
            throw;
        }
    }

    private static string? Normalize(string action, double? value) => action switch
    {
        "impression" or "watched" or "already_watched" or "skip" or "swipe_left" => "read",
        "swipe_right" or "favorite" or "more_like_this" => "positive",
        "not_for_me" or "less_like_this" or "not_interested" => "negative",
        "rating" or "rate" or "rate_inline" when value >= 9 => "strong_positive",
        "rating" or "rate" or "rate_inline" when value >= 7 => "positive",
        "rating" or "rate" or "rate_inline" when value <= 5 => "negative",
        "rating" or "rate" or "rate_inline" => "read",
        _ => null
    };

    private async Task EnsureGroup(CancellationToken ct)
    {
        try { await database.StreamCreateConsumerGroupAsync(Stream, Group, "0-0", createStream: true); }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { }
        await Task.CompletedTask;
    }

    private static string Value(StreamEntry entry, string name) => entry.Values.FirstOrDefault(x => x.Name == name).Value.ToString();
}
