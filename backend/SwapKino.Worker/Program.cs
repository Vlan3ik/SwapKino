using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SwapKino.Api;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<SwapKinoDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default") ?? builder.Configuration["DATABASE_URL"]));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration["REDIS_URL"] ?? "redis-runtime:6379,abortConnect=false"));
builder.Services.AddHttpClient("selenium", c => c.BaseAddress = new Uri(builder.Configuration["SELENIUM_URL"] ?? "http://selenium-service:8081"));
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<ImportStreamWorker>();
var host = builder.Build();
await host.RunAsync();

public abstract class RedisWorker(IServiceScopeFactory scopes, IConnectionMultiplexer redis, ILogger log) : BackgroundService
{
    protected const string Stream = "swapkino:events";
    protected readonly IServiceScopeFactory Scopes = scopes;
    protected readonly IDatabase Redis = redis.GetDatabase();
    protected readonly ILogger Log = log;
}

public sealed class OutboxDispatcher(IServiceScopeFactory scopes, IConnectionMultiplexer redis, ILogger<OutboxDispatcher> log)
    : RedisWorker(scopes, redis, log)
{
    private readonly string workerId = $"dispatcher-{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Redis.StringSetAsync("swapkino:worker:heartbeat", workerId, TimeSpan.FromSeconds(15));
                using var scope = Scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
                foreach (var item in await ClaimEvents(db, stoppingToken))
                {
                    try
                    {
                        await Redis.StreamAddAsync(Stream, new[]
                        {
                            new NameValueEntry("event_id", item.Id.ToString()),
                            new NameValueEntry("topic", item.Topic),
                            new NameValueEntry("payload", item.Payload),
                        });
                        item.Published = true;
                        item.PublishedAt = DateTime.UtcNow;
                        item.LockedBy = null;
                        item.LockedUntil = null;
                        item.LastError = null;
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        item.LastError = ex.Message;
                        item.NextAttemptAt = DateTime.UtcNow.AddSeconds(Math.Min(300, 2 * Math.Pow(2, item.AttemptCount)));
                        item.LockedBy = null;
                        item.LockedUntil = null;
                        await db.SaveChangesAsync(stoppingToken);
                        Log.LogError(ex, "Outbox event {EventId} failed on attempt {Attempt}", item.Id, item.AttemptCount);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Outbox dispatcher cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task<List<OutboxEvent>> ClaimEvents(SwapKinoDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var until = now.AddMinutes(2);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""OutboxEvents""
            SET ""LockedBy"" = {workerId}, ""LockedUntil"" = {until}, ""AttemptCount"" = ""AttemptCount"" + 1
            WHERE ""Id"" IN (
                SELECT ""Id"" FROM ""OutboxEvents""
                WHERE NOT ""Published""
                  AND (""NextAttemptAt"" IS NULL OR ""NextAttemptAt"" <= {now})
                  AND (""LockedUntil"" IS NULL OR ""LockedUntil"" <= {now})
                ORDER BY ""CreatedAt""
                FOR UPDATE SKIP LOCKED
                LIMIT 20
            )", ct);
        var events = await db.OutboxEvents.Where(x => !x.Published && x.LockedBy == workerId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return events;
    }
}

public sealed class ImportStreamWorker(
    IServiceScopeFactory scopes,
    IConnectionMultiplexer redis,
    IHttpClientFactory http,
    ILogger<ImportStreamWorker> log) : RedisWorker(scopes, redis, log)
{
    private const string Group = "swapkino-imports";
    private readonly string consumer = $"import-{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureConsumerGroup(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Возвращаем себе сообщения, оставшиеся без ACK после падения процесса.
                var claimed = await Redis.StreamAutoClaimAsync(Stream, Group, consumer, 120_000, "0-0", 10);
                foreach (var entry in claimed.ClaimedEntries)
                    await ProcessEntry(entry, stoppingToken);

                var entries = await Redis.StreamReadGroupAsync(Stream, Group, consumer, ">", 10);
                foreach (var entry in entries)
                    await ProcessEntry(entry, stoppingToken);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("NOGROUP", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureConsumerGroup(stoppingToken);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Import stream cycle failed");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
        }
    }

    private async Task EnsureConsumerGroup(CancellationToken ct)
    {
        try
        {
            await Redis.StreamCreateConsumerGroupAsync(Stream, Group, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { }
        await Task.CompletedTask;
    }

    private async Task ProcessEntry(StreamEntry entry, CancellationToken ct)
    {
        var topic = entry.Values.FirstOrDefault(x => x.Name == "topic").Value.ToString();
        try
        {
            if (topic == "kinopoisk.import")
            {
                var payload = JsonSerializer.Deserialize<ImportPayload>(Value(entry, "payload"));
                if (payload is not null)
                    await ProcessImport(payload, ct);
            }
            else if (topic == "kinopoisk.import.resume")
            {
                var payload = JsonSerializer.Deserialize<ResumePayload>(Value(entry, "payload"));
                if (payload is not null)
                    await ProcessResume(payload, ct);
            }

            // Нерелевантные для import-worker события тоже подтверждаем, чтобы
            // они не оставались бесконечно в pending-list consumer group.
            await Redis.StreamAcknowledgeAsync(Stream, Group, entry.Id);
        }
        catch (Exception ex)
        {
            var pending = await Redis.StreamPendingMessagesAsync(Stream, Group, 1, consumer, entry.Id, entry.Id);
            var deliveries = pending.Length == 0 ? 1 : pending[0].DeliveryCount;
            if (deliveries >= 5)
            {
                await Redis.StreamAddAsync("swapkino:events:dead-letter", new[]
                {
                    new NameValueEntry("event_id", Value(entry, "event_id")),
                    new NameValueEntry("topic", topic),
                    new NameValueEntry("payload", Value(entry, "payload")),
                    new NameValueEntry("error", ex.Message),
                    new NameValueEntry("attempts", deliveries),
                });
                await Redis.StreamAcknowledgeAsync(Stream, Group, entry.Id);
                Log.LogError(ex, "Import event {EventId} moved to dead-letter after {Attempts} attempts", entry.Id, deliveries);
            }
            else
            {
                // Без ACK сообщение будет автоматически перехвачено после lease.
                // Idempotent staging и статус ImportJob делают повтор безопасным.
                Log.LogError(ex, "Import event {EventId} failed on delivery {Attempt}; it will be reclaimed", entry.Id, deliveries);
            }
        }
    }

    private async Task ProcessImport(ImportPayload payload, CancellationToken ct)
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        var job = await db.ImportJobs.FindAsync([payload.JobId], ct);
        if (job is null || job.Status is "Completed" or "WaitingForUser") return;
        job.Status = "Running";
        job.Progress = Math.Max(job.Progress, 5);
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await RunImport(db, job, payload.ProfileUrl, ct);
    }

    private async Task ProcessResume(ResumePayload payload, CancellationToken ct)
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        var job = await db.ImportJobs.FindAsync([payload.JobId], ct);
        if (job is null || job.Status is "Completed" or "Cancelled") return;
        job.Status = "Running";
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        try
        {
            using var response = await http.CreateClient("selenium").PostAsync($"/api/v1/kinopoisk/captcha/{Uri.EscapeDataString(payload.SessionId)}/resume", null, ct);
            await HandleImportResponse(db, job, response, ct);
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.Error = ex.Message;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task RunImport(SwapKinoDbContext db, ImportJob job, string profileUrl, CancellationToken ct)
    {
        try
        {
            using var response = await http.CreateClient("selenium").PostAsJsonAsync(
                "/api/v1/kinopoisk/ratings",
                new { profile_url = profileUrl, include_unrated = true },
                ct);
            await HandleImportResponse(db, job, response, ct);
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.Error = ex.Message;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private static async Task HandleImportResponse(SwapKinoDbContext db, ImportJob job, HttpResponseMessage response, CancellationToken ct)
    {
        if ((int)response.StatusCode == 409)
        {
            job.Status = "WaitingForUser";
            job.Progress = 25;
            job.Checkpoint = await response.Content.ReadAsStringAsync(ct);
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        if (!response.IsSuccessStatusCode)
        {
            job.Status = "Failed";
            job.Error = $"Selenium returned {(int)response.StatusCode}";
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        var payload = await response.Content.ReadFromJsonAsync<RatingsResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Selenium returned an empty response");
        var existing = await db.ImportItems.Where(x => x.ImportJobId == job.Id).ToDictionaryAsync(x => x.KinopoiskUrl, ct);
        foreach (var item in payload.Items)
        {
            if (existing.ContainsKey(item.KinopoiskUrl)) continue;
            var row = new ImportItem { ImportJobId = job.Id, KinopoiskUrl = item.KinopoiskUrl, Title = item.Title, Year = item.Year, Genres = item.Genres, Rating = item.Rating, Kind = item.Kind, Page = item.Page };
            db.ImportItems.Add(row);
            existing[item.KinopoiskUrl] = row;
        }
        await db.SaveChangesAsync(ct);
        await ApplyMatches(db, job, existing.Values, ct);
        job.ImportedCount = existing.Count;
        job.Progress = 100;
        job.Status = "Completed";
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static async Task ApplyMatches(SwapKinoDbContext db, ImportJob job, IEnumerable<ImportItem> items, CancellationToken ct)
    {
        var movies = await db.Movies.AsNoTracking().ToListAsync(ct);
        foreach (var item in items.Where(x => x.MatchStatus == "pending"))
        {
            var match = movies.FirstOrDefault(movie => string.Equals(Normalize(movie.Title), Normalize(item.Title), StringComparison.OrdinalIgnoreCase) && (item.Year is null || movie.ReleaseDate is null || movie.ReleaseDate.StartsWith(item.Year.Value.ToString(), StringComparison.Ordinal)));
            if (match is null) { item.MatchStatus = "unmatched"; item.MatchError = "TMDB-фильм не найден в локальном каталоге"; continue; }
            item.TmdbId = match.TmdbId;
            item.MatchStatus = "matched";
            var actionType = item.Rating is null ? "watched" : "rate";
            var key = $"import:{job.Id}:{item.KinopoiskUrl}";
            if (!await db.UserActions.AnyAsync(x => x.UserId == job.UserId && x.IdempotencyKey == key, ct)) db.UserActions.Add(new UserAction { UserId = job.UserId, TmdbId = match.TmdbId, ActionType = actionType, Value = item.Rating, IdempotencyKey = key });
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Value(StreamEntry entry, string name) => entry.Values.FirstOrDefault(x => x.Name == name).Value.ToString();
    private static string Normalize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    private sealed record ImportPayload(Guid JobId, Guid UserId, string ProfileUrl);
    private sealed record ResumePayload(Guid JobId, Guid UserId, string ProfileUrl, string SessionId);
    private sealed record RatingsResponse(int Total, int Rated, int Unrated, [property: JsonPropertyName("items")] List<RatingItem> Items);
    private sealed record RatingItem(string Title, int? Year, string? Genres, double? Rating, string Kind, [property: JsonPropertyName("kinopoisk_url")] string KinopoiskUrl, int Page);
}
