using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SwapKino.Api;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<SwapKinoDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default") ?? builder.Configuration["DATABASE_URL"]));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration["REDIS_URL"] ?? "redis-runtime:6379,abortConnect=false"));
builder.Services.AddHttpClient("gorse", c =>
{
    c.BaseAddress = new Uri((builder.Configuration["GORSE_URL"] ?? "http://gorse-server:8087/").TrimEnd('/') + "/");
    c.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddScoped<RecommendationGateway>();
builder.Services.AddHttpClient("selenium", c => c.BaseAddress = new Uri(builder.Configuration["SELENIUM_URL"] ?? "http://selenium-service:8081"));
builder.Services.AddHttpClient("tmdb", c => c.BaseAddress = new Uri((builder.Configuration["TMDB_BASE_URL"] ?? "https://api.themoviedb.org/3").TrimEnd('/') + "/"));
builder.Services.AddScoped<TmdbClient>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<ImportStreamWorker>();
builder.Services.AddHostedService<RecommendationWorker>();
builder.Services.AddHostedService<RecommendationCatalogWorker>();
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
    private const int ImportBatchSize = 25;
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
        // This projection deliberately excludes Payload and every details-only
        // column. A catalog with multi-megabyte payloads remains cheap to match.
        var localMovies = await ImportQueries.LightweightMovies(db.Movies.AsNoTracking()).ToListAsync(ct);
        var total = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId, ct);
        var processed = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId && x.MatchStatus != "pending", ct);
        while (true)
        {
            var items = await db.ImportItems.AsNoTracking().Where(x => x.ImportJobId == jobId && x.MatchStatus == "pending")
                .OrderBy(x => x.Page).ThenBy(x => x.ExternalId).Take(ImportBatchSize).ToListAsync(ct);
            if (items.Count == 0) break;
            foreach (var item in items)
            {
                Movie? match = FindLocalMatch(localMovies, item);
                if (match is null || MatchScore(match, item) < RequiredScore(item))
                {
                    // A low-confidence local candidate must not become a match
                    // merely because the remote lookup failed.
                    match = null;
                    try
                    {
                        var remote = await tmdb.SearchAsync(item.Title, item.IsSeries, ct);
                        match = SelectConfidentMatch(remote.Results, item);
                        if (match is not null)
                        {
                            // Search results are detached candidates. Persist and enrich
                            // only the candidate that passed the confidence checks.
                            var detailed = await tmdb.Details(match.TmdbId, ct, match.IsSeries);
                            match = LightweightMatch(detailed);
                            db.ChangeTracker.Clear(); // do not retain the heavy details Payload
                            var cached = localMovies.FirstOrDefault(x => x.TmdbId == match.TmdbId && x.IsSeries == match.IsSeries);
                            if (cached is null) localMovies.Add(match);
                            else CopyMatchFields(cached, match);
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        item.MatchError = $"TMDB недоступен: {ex.Message}";
                    }
                }
                var status = match is null ? "unmatched" : "matched";
                var error = match is null ? item.MatchError ?? "TMDB-фильм не найден" : null;
                await db.ImportItems.Where(x => x.Id == item.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.TmdbId, match == null ? (int?)null : match.TmdbId)
                    .SetProperty(x => x.MatchStatus, status)
                    .SetProperty(x => x.MatchError, error), ct);
                processed++;
            }
            await SaveJobProgress(db, jobId, "Matching", 40, 75, processed, total, started, ct);
            db.ChangeTracker.Clear();
        }

        var job = await db.ImportJobs.FindAsync([jobId], ct) ?? throw new InvalidOperationException("Import job disappeared");
        SetPhase(job, "Applying", 75, 0);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var profileId = ProfileId(job.ProfileUrl);
        var userId = job.UserId;
        var applied = 0;
        var index = 0;
        while (index < total)
        {
            var items = await db.ImportItems.AsNoTracking().Where(x => x.ImportJobId == jobId)
                .OrderBy(x => x.Page).ThenBy(x => x.ExternalId).Skip(index).Take(ImportBatchSize).ToListAsync(ct);
            if (items.Count == 0) break;
            foreach (var item in items)
            {
                var external = await db.UserExternalItems.FindAsync([userId, "kinopoisk", profileId, item.ExternalId], ct);
                if (external is null)
                {
                    external = new UserExternalItem { UserId = userId, Source = "kinopoisk", ProfileId = profileId, ExternalId = item.ExternalId };
                    db.UserExternalItems.Add(external);
                }
                external.TmdbId = item.TmdbId;
                external.IsSeries = item.IsSeries;
                external.Rating = item.Rating;
                external.Watched = true;
                external.MatchStatus = item.MatchStatus;
                external.MatchError = item.MatchError;
                external.UpdatedAt = DateTime.UtcNow;

                if (item.MatchStatus != "matched" || item.TmdbId is null)
                {
                    index++;
                    continue;
                }

                var actionType = item.Rating is null ? "watched" : "rate";
                var key = $"kinopoisk:{profileId}:{item.ExternalId}";
                var action = await db.UserActions.FirstOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == key, ct);
                if (action is null)
                {
                    action = new UserAction { UserId = userId, IdempotencyKey = key };
                    db.UserActions.Add(action);
                }
                action.TmdbId = item.TmdbId!.Value;
                action.IsSeries = item.IsSeries;
                action.ActionType = actionType;
                action.Value = item.Rating;
                action.CreatedAt = DateTime.UtcNow;

                var state = await db.UserMovieStates.FindAsync([userId, item.TmdbId.Value, item.IsSeries], ct);
                if (state is null)
                {
                    state = new UserMovieState { UserId = userId, TmdbId = item.TmdbId.Value, IsSeries = item.IsSeries };
                    db.UserMovieStates.Add(state);
                }
                state.Watched = true;
                state.Rating = item.Rating;
                state.UpdatedAt = DateTime.UtcNow;
                applied++;
                index++;
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            job = await db.ImportJobs.FindAsync([jobId], ct) ?? throw new InvalidOperationException("Import job disappeared");
            job.AppliedCount = applied;
            UpdateProgress(job, "Applying", 75, 99, index, total, started);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private static Movie? FindLocalMatch(IEnumerable<Movie> candidates, ImportItem item)
    {
        var normalized = Normalize(item.Title);
        return candidates.Where(x => x.IsSeries == item.IsSeries).Select(x => (Movie: x, Score: MatchScore(x, item)))
            .Where(x => Normalize(x.Movie.Title) == normalized || Normalize(x.Movie.OriginalTitle ?? "") == normalized)
            .OrderByDescending(x => x.Score).Select(x => x.Movie).FirstOrDefault();
    }

    private static Movie LightweightMatch(Movie movie) => new()
    {
        TmdbId = movie.TmdbId,
        IsSeries = movie.IsSeries,
        Title = movie.Title,
        OriginalTitle = movie.OriginalTitle,
        ReleaseDate = movie.ReleaseDate,
        VoteCount = movie.VoteCount
    };

    private static void CopyMatchFields(Movie target, Movie source)
    {
        target.Title = source.Title;
        target.OriginalTitle = source.OriginalTitle;
        target.ReleaseDate = source.ReleaseDate;
        target.VoteCount = source.VoteCount;
    }

    private static async Task SaveJobProgress(SwapKinoDbContext db, Guid jobId, string phase, int from, int to, int done, int total, DateTime started, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var job = await db.ImportJobs.FindAsync([jobId], ct) ?? throw new InvalidOperationException("Import job disappeared");
        job.MatchedCount = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId && x.MatchStatus == "matched", ct);
        job.UnmatchedCount = await db.ImportItems.CountAsync(x => x.ImportJobId == jobId && x.MatchStatus == "unmatched", ct);
        UpdateProgress(job, phase, from, to, done, total, started);
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
        if (item.Year is not null && int.TryParse(releaseYear, out var year))
            yearScore = year == item.Year ? 0.25 : Math.Abs(year - item.Year.Value) == 1 ? 0.10 : -0.20;
        return Math.Clamp(titleScore + yearScore + Math.Min(movie.VoteCount / 100_000d, 0.03), 0, 1);
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
}
