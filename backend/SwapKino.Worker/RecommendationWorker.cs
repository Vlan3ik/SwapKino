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
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
            var profile = await RecommendationProfileBuilder.BuildAsync(db, userId, ct);
            await db.SaveChangesAsync(ct);
            await database.StringSetAsync($"{RecommendationProfileBuilder.ProfileCachePrefix}{userId}:profile", JsonSerializer.Serialize(profile), TimeSpan.FromHours(24));
            if (document.RootElement.TryGetProperty("sessionId", out var sessionNode) && sessionNode.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(sessionNode.GetString()))
            {
                var sessionKey = $"rec:session:{userId}:{sessionNode.GetString()}";
                await database.HashSetAsync(sessionKey, [
                    new HashEntry("sessionProfileVersion", profile.ProfileVersion),
                    new HashEntry("sessionProfileUpdatedAt", DateTime.UtcNow.ToString("O")),
                    new HashEntry("feedRefreshRequested", 1)
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

    private async Task EnsureGroup(CancellationToken ct)
    {
        try { await database.StreamCreateConsumerGroupAsync(Stream, Group, "0-0", createStream: true); }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { }
        await Task.CompletedTask;
    }

    private static string Value(StreamEntry entry, string name) => entry.Values.FirstOrDefault(x => x.Name == name).Value.ToString();
}
