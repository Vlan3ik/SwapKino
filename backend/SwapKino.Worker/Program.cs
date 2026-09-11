using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SwapKino.Api;
using SwapKino.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<SwapKinoDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default") is { Length: > 0 } connection ? connection : builder.Configuration["DATABASE_URL"]));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration["REDIS_URL"] ?? "redis-runtime:6379,abortConnect=false"));
builder.Services.AddHttpClient("selenium", c => c.BaseAddress = new Uri(builder.Configuration["SELENIUM_URL"] ?? "http://selenium-service:8081"));
builder.Services.AddHttpClient("tmdb", c => c.BaseAddress = new Uri((builder.Configuration["TMDB_BASE_URL"] ?? "https://api.themoviedb.org/3").TrimEnd('/') + "/"));
builder.Services.AddScoped<TmdbClient>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<ImportStreamWorker>();
builder.Services.AddHostedService<TasteProfileWorker>();
var host = builder.Build();
await host.RunAsync();

namespace SwapKino.Worker
{

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
        await EnsureConsumerGroup("swapkino-recommendations");
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
                await CleanupPublishedEvents(db, stoppingToken);
                // Never trim while either consumer group has pending entries.
                // Approximate MAXLEN can otherwise discard an event that is still
                // recoverable after a long outage.
                var recommendationPending = await Redis.StreamPendingAsync(Stream, "swapkino-recommendations");
                var importPending = await Redis.StreamPendingAsync(Stream, "swapkino-imports");
                var tastePending = await Redis.StreamPendingAsync(Stream, "swapkino-taste");
                if (recommendationPending.PendingMessageCount == 0 && importPending.PendingMessageCount == 0 && tastePending.PendingMessageCount == 0)
                    await Redis.StreamTrimAsync(Stream, 100_000, useApproximateMaxLength: false);
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Outbox dispatcher cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task EnsureConsumerGroup(string group)
    {
        try
        {
            await Redis.StreamCreateConsumerGroupAsync(Stream, group, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            Log.LogDebug(ex, "Redis consumer group already exists: {Group}", group);
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

    private static async Task CleanupPublishedEvents(SwapKinoDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var ids = await db.OutboxEvents.Where(x => x.Published && x.PublishedAt < cutoff).OrderBy(x => x.PublishedAt).Take(1000).Select(x => x.Id).ToListAsync(ct);
        if (ids.Count > 0) await db.OutboxEvents.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }
}

public sealed class ImportStreamWorker(
    IServiceScopeFactory scopes,
    IConnectionMultiplexer redis,
    IHttpClientFactory http,
    ILogger<ImportStreamWorker> log) : RedisWorker(scopes, redis, log)
{
    private const string Group = "swapkino-imports";
    private const int ImportBatchSize = 25;
    private readonly string consumer = $"import-{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureConsumerGroup();
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
                await EnsureConsumerGroup();
            }
            catch (Exception ex)
            {
                Log.LogError(ex, "Import stream cycle failed");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
        }
    }

    private async Task EnsureConsumerGroup()
    {
        try
        {
            await Redis.StreamCreateConsumerGroupAsync(Stream, Group, "0-0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase)) { Log.LogDebug(ex, "Redis consumer group already exists: {Group}", Group); }
    }

    private async Task ProcessEntry(StreamEntry entry, CancellationToken ct)
    {
        var topic = entry.Values.FirstOrDefault(x => x.Name == "topic").Value.ToString();
        Log.LogInformation("Processing stream event {EventId} topic {Topic}", entry.Id, topic);
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
        if (job is null)
        {
            Log.LogWarning("Import job {JobId} was not found for stream payload", payload.JobId);
            return;
        }
        if (job.Status is "Completed" or "CompletedWithWarnings" or "Failed" or "Cancelled" or "WaitingForUser")
        {
            Log.LogInformation("Import job {JobId} already has terminal/intermediate status {Status}", job.Id, job.Status);
            return;
        }
        if (await HasStagedImport(db, job, ct))
        {
            Log.LogInformation("Continuing staged Kinopoisk import job {JobId} without scraping", job.Id);
            await ContinueStagedImport(db, job, scope.ServiceProvider.GetRequiredService<TmdbClient>(), ct);
            return;
        }
        Log.LogInformation("Starting Kinopoisk import job {JobId} from {ProfileUrl}", job.Id, payload.ProfileUrl);
        SetPhase(job, "Scraping", 5, 0);
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await RunImport(db, job, payload.ProfileUrl, scope.ServiceProvider.GetRequiredService<TmdbClient>(), ct);
    }

    private async Task ProcessResume(ResumePayload payload, CancellationToken ct)
    {
        using var scope = Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        var job = await db.ImportJobs.FindAsync([payload.JobId], ct);
        if (job is null || job.Status is "Completed" or "CompletedWithWarnings" or "Cancelled") return;
        if (await HasStagedImport(db, job, ct))
        {
            Log.LogInformation("Resuming staged Kinopoisk import job {JobId} from checkpoint without Selenium", job.Id);
            await ContinueStagedImport(db, job, scope.ServiceProvider.GetRequiredService<TmdbClient>(), ct);
            return;
        }
        SetPhase(job, "Scraping", Math.Max(job.Progress, 5), 0);
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(payload.SessionId))
                throw new InvalidOperationException("Сессия CAPTCHA недоступна и staged-данных для resume нет");
            using var response = await http.CreateClient("selenium").PostAsync($"/api/v1/kinopoisk/captcha/{Uri.EscapeDataString(payload.SessionId)}/resume", null, ct);
            await HandleImportResponse(db, job, scope.ServiceProvider.GetRequiredService<TmdbClient>(), response, ct);
        }
        catch (Exception ex)
        {
            await MarkFailed(db, job.Id, ex, ct);
            Log.LogError(ex, "Kinopoisk import job {JobId} failed while resuming", job.Id);
        }
    }

    private async Task RunImport(SwapKinoDbContext db, ImportJob job, string profileUrl, TmdbClient tmdb, CancellationToken ct)
    {
        try
        {
            using var response = await http.CreateClient("selenium").PostAsJsonAsync(
                "/api/v1/kinopoisk/ratings",
                new { profile_url = profileUrl, include_unrated = true },
                ct);
            Log.LogInformation("Kinopoisk import job {JobId} received Selenium status {StatusCode}", job.Id, (int)response.StatusCode);
            await HandleImportResponse(db, job, tmdb, response, ct);
        }
        catch (Exception ex)
        {
            await MarkFailed(db, job.Id, ex, ct);
            Log.LogError(ex, "Kinopoisk import job {JobId} failed", job.Id);
        }
    }

    private static async Task<bool> HasStagedImport(SwapKinoDbContext db, ImportJob job, CancellationToken ct)
    {
        if (!await db.ImportItems.AsNoTracking().AnyAsync(x => x.ImportJobId == job.Id, ct)) return false;
        if (job.Progress >= 40 || job.DiscoveredCount > 0 || job.Phase is "Matching" or "Applying") return true;
        try
        {
            using var checkpoint = JsonDocument.Parse(job.Checkpoint);
            var phase = checkpoint.RootElement.TryGetProperty("phase", out var value) ? value.GetString() : null;
            return phase is "Matching" or "Applying";
        }
        catch (JsonException) { return false; }
    }

    private static async Task ContinueStagedImport(SwapKinoDbContext db, ImportJob job, TmdbClient tmdb, CancellationToken ct)
    {
        try
        {
            job.Error = null;
            await db.SaveChangesAsync(ct);
            await MatchAndApply(db, job.Id, tmdb, ct);
            await CompleteImport(db, job.Id, ct);
        }
        catch (Exception ex)
        {
            await MarkFailed(db, job.Id, ex, ct);
            throw;
        }
    }

    private static async Task CompleteImport(SwapKinoDbContext db, Guid jobId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var job = await db.ImportJobs.FindAsync([jobId], ct) ?? throw new InvalidOperationException("Import job disappeared");
        job.ImportedCount = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId, ct);
        job.DiscoveredCount = Math.Max(job.DiscoveredCount, job.ImportedCount);
        job.MatchedCount = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId && x.MatchStatus == "matched", ct);
        job.UnmatchedCount = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId && x.MatchStatus == "unmatched", ct);
        job.AppliedCount = job.MatchedCount;
        job.Progress = 100;
        job.PhaseProgress = 100;
        job.EstimatedRemainingSeconds = 0;
        job.Status = job.UnmatchedCount > 0 ? "CompletedWithWarnings" : "Completed";
        job.Phase = job.Status;
        job.Error = null;
        job.Checkpoint = JsonSerializer.Serialize(new { phase = job.Phase, job.Progress, job.DiscoveredCount, job.MatchedCount, job.AppliedCount, job.UnmatchedCount, job.PagesProcessed, job.PagesTotal, job.EstimatedRemainingSeconds });
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static async Task MarkFailed(SwapKinoDbContext db, Guid jobId, Exception ex, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var job = await db.ImportJobs.FindAsync([jobId], ct);
        if (job is null) return;
        job.Status = "Failed";
        // Keep Phase and Checkpoint at the resumable operation (Matching/Applying).
        job.Error = ex.Message;
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task HandleImportResponse(SwapKinoDbContext db, ImportJob job, TmdbClient tmdb, HttpResponseMessage response, CancellationToken ct)
    {
        if ((int)response.StatusCode == 409)
        {
            job.Status = "WaitingForUser";
            job.Phase = "Scraping";
            job.PhaseProgress = 25;
            job.Progress = 25;
            job.Checkpoint = await response.Content.ReadAsStringAsync(ct);
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        if (!response.IsSuccessStatusCode)
        {
            job.Status = "Failed";
            job.Phase = "Failed";
            var detail = await response.Content.ReadAsStringAsync(ct);
            job.Error = $"Selenium returned {(int)response.StatusCode}: {detail}";
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }
        var payload = await response.Content.ReadFromJsonAsync<RatingsResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Selenium returned an empty response");
        if (!payload.Complete || payload.PagesProcessed < payload.PagesTotal)
            throw new InvalidOperationException($"Кинопоиск вернул неполный набор: {payload.PagesProcessed}/{payload.PagesTotal} страниц");
        job.PagesProcessed = payload.PagesProcessed;
        job.PagesTotal = payload.PagesTotal;
        job.DiscoveredCount = payload.Items.Count;
        SetPhase(job, "Matching", 40, 0);
        var existing = await db.ImportItems.Where(x => x.ImportJobId == job.Id).ToDictionaryAsync(x => x.ExternalId, ct);
        foreach (var item in payload.Items)
        {
            if (!Regex.IsMatch(item.ExternalId, "^[0-9]+$"))
                throw new InvalidOperationException($"Невалидный Kinopoisk ID: {item.ExternalId}");
            if (!existing.TryGetValue(item.ExternalId, out var row))
            {
                row = new ImportItem { ImportJobId = job.Id, ExternalId = item.ExternalId };
                db.ImportItems.Add(row);
                existing[item.ExternalId] = row;
            }
            row.KinopoiskUrl = item.KinopoiskUrl;
            row.Title = item.Title;
            row.Year = item.Year;
            row.Genres = item.Genres;
            row.Rating = item.Rating;
            row.Kind = item.Kind;
            row.IsSeries = item.Kind == "series";
            row.Page = item.Page;
            // Existing matched/unmatched rows are checkpoints. Re-scraping may
            // refresh metadata, but must not discard completed matching work.
        }
        await db.SaveChangesAsync(ct);
        await MatchAndApply(db, job.Id, tmdb, ct);
        await CompleteImport(db, job.Id, ct);
    }

    private static async Task MatchAndApply(SwapKinoDbContext db, Guid jobId, TmdbClient tmdb, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var total = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId, ct);
        await MatchPendingItems(db, jobId, tmdb, total, started, ct);

        var job = await db.ImportJobs.FindAsync([jobId], ct) ?? throw new InvalidOperationException("Import job disappeared");
        SetPhase(job, "Applying", 75, 0);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        await ApplyMatchedItems(db, job, total, started, ct);
    }

    private static async Task MatchPendingItems(SwapKinoDbContext db, Guid jobId, TmdbClient tmdb, int total, DateTime started, CancellationToken ct)
    {
        var processed = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId && x.MatchStatus != "pending", ct);
        while (true)
        {
            var items = await db.ImportItems.AsNoTracking().Where(x => x.ImportJobId == jobId && x.MatchStatus == "pending")
                .OrderBy(x => x.Page).ThenBy(x => x.ExternalId).Take(ImportBatchSize).ToListAsync(ct);
            if (items.Count == 0) return;
            foreach (var item in items)
            {
                await MatchItem(db, tmdb, item, ct);
                processed++;
            }
            await SaveJobProgress(db, new ProgressRequest(jobId, "Matching", 40, 75, processed, total, started), ct);
            db.ChangeTracker.Clear();
        }
    }

    private static async Task MatchItem(SwapKinoDbContext db, TmdbClient tmdb, ImportItem item, CancellationToken ct)
    {
        TmdbImportCandidate? match = null;
        try { match = SelectConfidentImportMatch(await tmdb.SearchCandidatesAsync(item.Title, item.IsSeries, ct), item); }
        catch (HttpRequestException ex) { item.MatchError = $"TMDB недоступен: {ex.Message}"; }
        var status = match is null ? "unmatched" : "matched";
        var error = match is null ? item.MatchError ?? "TMDB-фильм не найден" : null;
        await db.ImportItems.Where(x => x.Id == item.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.TmdbId, match == null ? (int?)null : match.TmdbId)
            .SetProperty(x => x.MatchStatus, status)
            .SetProperty(x => x.MatchError, error), ct);
    }

    private static async Task ApplyMatchedItems(SwapKinoDbContext db, ImportJob job, int total, DateTime started, CancellationToken ct)
    {
        var profileId = ProfileId(job.ProfileUrl);
        var index = 0;
        var applied = 0;
        while (index < total)
        {
            var items = await db.ImportItems.AsNoTracking().Where(x => x.ImportJobId == job.Id).OrderBy(x => x.Page).ThenBy(x => x.ExternalId).Skip(index).Take(ImportBatchSize).ToListAsync(ct);
            if (items.Count == 0) return;
            foreach (var item in items)
            {
                await ApplyItem(db, job.UserId, profileId, item, ct);
                if (item.MatchStatus == "matched" && item.TmdbId is not null) applied++;
                index++;
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            job = await db.ImportJobs.FindAsync([job.Id], ct) ?? throw new InvalidOperationException("Import job disappeared");
            job.AppliedCount = applied;
            UpdateProgress(job, "Applying", 75, 99, index, total, started);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private static async Task ApplyItem(SwapKinoDbContext db, Guid userId, string profileId, ImportItem item, CancellationToken ct)
    {
        var external = await db.UserExternalItems.FindAsync([userId, "kinopoisk", profileId, item.ExternalId], ct)
            ?? new UserExternalItem { UserId = userId, Source = "kinopoisk", ProfileId = profileId, ExternalId = item.ExternalId };
        external.TmdbId = item.TmdbId; external.IsSeries = item.IsSeries; external.Rating = item.Rating; external.Watched = true;
        external.MatchStatus = item.MatchStatus; external.MatchError = item.MatchError; external.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(external).State == EntityState.Detached) db.UserExternalItems.Add(external);
        if (item.MatchStatus != "matched" || item.TmdbId is null) return;

        var key = $"kinopoisk:{profileId}:{item.ExternalId}";
        var action = await db.UserActions.FirstOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == key, ct)
            ?? new UserAction { UserId = userId, IdempotencyKey = key };
        action.TmdbId = item.TmdbId.Value; action.IsSeries = item.IsSeries; action.ActionType = item.Rating is null ? "watched" : "rate";
        action.Value = item.Rating; action.CreatedAt = DateTime.UtcNow;
        if (db.Entry(action).State == EntityState.Detached) db.UserActions.Add(action);
        var state = await db.UserMovieStates.FindAsync([userId, item.TmdbId.Value, item.IsSeries], ct)
            ?? new UserMovieState { UserId = userId, TmdbId = item.TmdbId.Value, IsSeries = item.IsSeries };
        state.Watched = true; state.Rating = item.Rating; state.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(state).State == EntityState.Detached) db.UserMovieStates.Add(state);
    }

    private static TmdbImportCandidate? SelectConfidentImportMatch(IEnumerable<TmdbImportCandidate> candidates, ImportItem item)
    {
        var ranked = candidates.Where(x => x.IsSeries == item.IsSeries).Select(x => (Candidate: x, Score: ImportMatchScore(x, item))).OrderByDescending(x => x.Score).ThenByDescending(x => x.Candidate.VoteCount).ToList();
        if (ranked.Count == 0 || ranked[0].Score < RequiredScore(item)) return null;
        if (ranked.Count > 1 && ranked[0].Score - ranked[1].Score < 0.08 && ranked[0].Score < 0.94) return null;
        return ranked[0].Candidate;
    }

    private static double ImportMatchScore(TmdbImportCandidate candidate, ImportItem item)
    {
        var wanted = Normalize(item.Title); var title = Normalize(candidate.Title); var original = Normalize(candidate.OriginalTitle ?? "");
        var titleScore = wanted == title || wanted == original ? 0.72 : 0.55 * TokenSimilarity(wanted, title); var yearScore = 0d;
        var releaseYear = candidate.ReleaseDate is { Length: >= 4 } ? candidate.ReleaseDate[..4] : null;
        yearScore = YearScore(item.Year, releaseYear);
        return Math.Clamp(titleScore + yearScore + Math.Min(candidate.VoteCount / 100_000d, 0.03), 0, 1);
    }

    private static async Task SaveJobProgress(SwapKinoDbContext db, ProgressRequest request, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var job = await db.ImportJobs.FindAsync([request.JobId], ct) ?? throw new InvalidOperationException("Import job disappeared");
        job.MatchedCount = await db.ImportItems.CountAsync(x => x.ImportJobId == request.JobId && x.MatchStatus == "matched", ct);
        job.UnmatchedCount = await db.ImportItems.CountAsync(x => x.ImportJobId == request.JobId && x.MatchStatus == "unmatched", ct);
        UpdateProgress(job, request.Phase, request.From, request.To, request.Done, request.Total, request.Started);
        await db.SaveChangesAsync(ct);
    }

    internal static Movie? SelectConfidentMatch(IEnumerable<Movie> candidates, ImportItem item)
    {
        var ranked = candidates.Where(x => x.IsSeries == item.IsSeries)
            .Select(x => (Movie: x, Score: MatchScore(x, item)))
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Movie.VoteCount).ToList();
        if (ranked.Count == 0 || ranked[0].Score < RequiredScore(item)) return null;
        if (ranked.Count > 1 && ranked[0].Score - ranked[1].Score < 0.08 && ranked[0].Score < 0.94) return null;
        return ranked[0].Movie;
    }

    internal static double MatchScore(Movie movie, ImportItem item)
    {
        var wanted = Normalize(item.Title);
        var title = Normalize(movie.Title);
        var original = Normalize(movie.OriginalTitle ?? "");
        var titleScore = wanted == title || wanted == original ? 0.72 : 0.55 * TokenSimilarity(wanted, title);
        var yearScore = 0d;
        var releaseYear = movie.ReleaseDate is { Length: >= 4 } ? movie.ReleaseDate[..4] : null;
        yearScore = YearScore(item.Year, releaseYear);
        return Math.Clamp(titleScore + yearScore + Math.Min(movie.VoteCount / 100_000d, 0.03), 0, 1);
    }

    private static double YearScore(int? expectedYear, string? releaseYear)
    {
        if (expectedYear is null || !int.TryParse(releaseYear, out var actualYear)) return 0;
        if (actualYear == expectedYear) return 0.25;
        return Math.Abs(actualYear - expectedYear.Value) == 1 ? 0.10 : -0.20;
    }

    private static double RequiredScore(ImportItem item) => item.Year is null ? 0.70 : 0.78;
    private static double TokenSimilarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0) return 0;
        if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
            return (double)Math.Min(left.Length, right.Length) / Math.Max(left.Length, right.Length);
        return 0;
    }

    private static string ProfileId(string profileUrl)
        => Regex.Match(profileUrl, @"/user/(\d+)", RegexOptions.IgnoreCase).Groups[1].Value is { Length: > 0 } id ? id : "unknown";

    private static void SetPhase(ImportJob job, string phase, int progress, int phaseProgress)
    {
        job.Status = phase;
        job.Phase = phase;
        job.Progress = progress;
        job.PhaseProgress = phaseProgress;
        job.EstimatedRemainingSeconds = null;
        job.Checkpoint = JsonSerializer.Serialize(new { phase, phaseProgress, progress, job.DiscoveredCount, job.MatchedCount, job.AppliedCount, job.UnmatchedCount, job.PagesProcessed, job.PagesTotal });
        job.UpdatedAt = DateTime.UtcNow;
    }

    private static void UpdateProgress(ImportJob job, string phase, int from, int to, int done, int total, DateTime started)
    {
        var fraction = total == 0 ? 1d : Math.Clamp((double)done / total, 0, 1);
        job.Phase = phase;
        job.Status = phase;
        job.PhaseProgress = (int)Math.Round(fraction * 100);
        job.Progress = from + (int)Math.Round((to - from) * fraction);
        var elapsed = Math.Max(1, (DateTime.UtcNow - started).TotalSeconds);
        job.EstimatedRemainingSeconds = done == 0 ? null : (int)Math.Ceiling(elapsed / done * Math.Max(0, total - done));
        job.Checkpoint = JsonSerializer.Serialize(new { phase, job.PhaseProgress, job.Progress, job.DiscoveredCount, job.MatchedCount, job.AppliedCount, job.UnmatchedCount, job.PagesProcessed, job.PagesTotal, job.EstimatedRemainingSeconds });
        job.UpdatedAt = DateTime.UtcNow;
    }

    private static string Value(StreamEntry entry, string name) => entry.Values.FirstOrDefault(x => x.Name == name).Value.ToString();
    private static string Normalize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    private sealed record ImportPayload(
        [property: JsonPropertyName("jobId")] Guid JobId,
        [property: JsonPropertyName("userId")] Guid UserId,
        [property: JsonPropertyName("profileUrl")] string ProfileUrl);
    private sealed record ResumePayload(
        [property: JsonPropertyName("jobId")] Guid JobId,
        [property: JsonPropertyName("userId")] Guid UserId,
        [property: JsonPropertyName("profileUrl")] string ProfileUrl,
        [property: JsonPropertyName("sessionId")] string? SessionId);
    private sealed record RatingsResponse(int Total, int Rated, int Unrated, [property: JsonPropertyName("pages_processed")] int PagesProcessed, [property: JsonPropertyName("pages_total")] int PagesTotal, bool Complete, [property: JsonPropertyName("items")] List<RatingItem> Items);
    private sealed record RatingItem([property: JsonPropertyName("external_id")] string ExternalId, string Title, int? Year, string? Genres, double? Rating, string Kind, [property: JsonPropertyName("kinopoisk_url")] string KinopoiskUrl, int Page);
    private sealed record ProgressRequest(Guid JobId, string Phase, int From, int To, int Done, int Total, DateTime Started);
}

}
