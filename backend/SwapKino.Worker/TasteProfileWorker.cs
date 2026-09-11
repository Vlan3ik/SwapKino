using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SwapKino.Api;

namespace SwapKino.Worker;

public sealed class TasteProfileWorker(IServiceScopeFactory scopes, IConnectionMultiplexer redis, ILogger<TasteProfileWorker> log) : BackgroundService
{
    private const string Stream = "swapkino:events"; private const string Group = "swapkino-taste";
    private readonly string consumer = $"taste-{Environment.MachineName}-{Guid.NewGuid():N}"; private readonly IDatabase database = redis.GetDatabase();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await database.StreamCreateConsumerGroupAsync(Stream, Group, "0-0", createStream: true); } catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { log.LogDebug(ex, "Redis consumer group already exists: {Group}", Group); }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await database.StreamReadGroupAsync(Stream, Group, consumer, ">", 20);
                foreach (var entry in entries)
                {
                    if (Value(entry, "topic") == "taste.profile.update") await Process(entry, stoppingToken);
                    else await database.StreamAcknowledgeAsync(Stream, Group, entry.Id);
                }
            }
            catch (Exception ex) { log.LogError(ex, "Taste profile cycle failed"); await Task.Delay(2000, stoppingToken); }
            await Task.Delay(500, stoppingToken);
        }
    }
    private async Task Process(StreamEntry entry, CancellationToken ct)
    {
        try
        {
            var update = ParseUpdate(Value(entry, "payload"));
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
            var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
            var features = await LoadFeatures(tmdb, update.TmdbId, update.IsSeries, ct);
            await ApplyFeatures(db, update, features, ct);
            await db.SaveChangesAsync(ct); await database.StreamAcknowledgeAsync(Stream, Group, entry.Id);
        }
        catch (Exception ex) { log.LogWarning(ex, "Taste update failed; event will be retried"); }
    }

    private static TasteUpdate ParseUpdate(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var rating = root.GetProperty("rating").GetDouble();
        return new(root.GetProperty("userId").GetGuid(), root.GetProperty("tmdbId").GetInt32(), root.GetProperty("isSeries").GetBoolean(), RatingSignal(rating));
    }

    private static double RatingSignal(double rating) => rating switch { >= 9 => 1.0, >= 7 => .55, >= 5 => 0.0, >= 3 => -.55, _ => -1.0 };

    private static async Task<IReadOnlyList<(FilmstripFeatureType Type, int Id)>> LoadFeatures(TmdbClient tmdb, int tmdbId, bool isSeries, CancellationToken ct)
    {
        using var details = await tmdb.Get($"/{(isSeries ? "tv" : "movie")}/{tmdbId}", new() { ["language"] = "ru-RU", ["append_to_response"] = "keywords" }, ct);
        var features = new List<(FilmstripFeatureType Type, int Id)>();
        AddFeatures(details.RootElement, "genres", FilmstripFeatureType.genre, features);
        if (details.RootElement.TryGetProperty("keywords", out var container) && container.ValueKind == JsonValueKind.Object)
        {
            AddFeatures(container, "keywords", FilmstripFeatureType.keyword, features);
            AddFeatures(container, "results", FilmstripFeatureType.keyword, features);
        }
        return features.Distinct().ToArray();
    }

    private static void AddFeatures(JsonElement root, string property, FilmstripFeatureType type, List<(FilmstripFeatureType Type, int Id)> target)
    {
        if (!root.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return;
        target.AddRange(values.EnumerateArray().Where(x => x.TryGetProperty("id", out _)).Select(x => (type, x.GetProperty("id").GetInt32())));
    }

    private static async Task ApplyFeatures(SwapKinoDbContext db, TasteUpdate update, IEnumerable<(FilmstripFeatureType Type, int Id)> features, CancellationToken ct)
    {
        foreach (var feature in features)
        {
            var row = await db.UserTasteFeatures.FindAsync([update.UserId, feature.Type, feature.Id], ct)
                ?? new UserTasteFeature { UserId = update.UserId, FeatureType = feature.Type, TmdbFeatureId = feature.Id };
            row.ObservationCount++;
            row.Weight += (update.Signal - row.Weight) / row.ObservationCount;
            row.Confidence = 1 - Math.Exp(-row.ObservationCount / 5d);
            row.UpdatedAt = DateTime.UtcNow;
            if (db.Entry(row).State == EntityState.Detached) db.UserTasteFeatures.Add(row);
        }
    }

    private sealed record TasteUpdate(Guid UserId, int TmdbId, bool IsSeries, double Signal);
    private static string Value(StreamEntry entry, string name) => entry.Values.FirstOrDefault(x => x.Name == name).Value.ToString();
}
