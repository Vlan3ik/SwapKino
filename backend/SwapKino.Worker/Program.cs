using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using SwapKino.Api;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<SwapKinoDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default") ?? builder.Configuration["DATABASE_URL"]));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(builder.Configuration["REDIS_URL"] ?? "redis-runtime:6379,abortConnect=false"));
builder.Services.AddHttpClient("selenium", c => c.BaseAddress = new Uri(builder.Configuration["SELENIUM_URL"] ?? "http://selenium-service:8081"));
builder.Services.AddHostedService<OutboxWorker>();
var host = builder.Build();
await host.RunAsync();

public sealed class OutboxWorker(IServiceScopeFactory scopes, IConnectionMultiplexer redis, IHttpClientFactory http, ILogger<OutboxWorker> log) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
                var events = await ClaimEvents(db, stoppingToken);
                foreach (var item in events)
                {
                    try
                    {
                        if (item.Topic == "kinopoisk.import")
                        {
                            var payload = JsonSerializer.Deserialize<ImportPayload>(item.Payload);
                            var job = payload is null ? null : await db.ImportJobs.FindAsync([payload.JobId], stoppingToken);
                            if (job is not null && job.Status is not "Completed" and not "WaitingForUser")
                            {
                                job.Status = "Running";
                                job.Progress = Math.Max(job.Progress, 5);
                                job.UpdatedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync(stoppingToken);
                                await RunImport(db, job, payload!.ProfileUrl, stoppingToken);
                            }
                        }

                        await redis.GetDatabase().StreamAddAsync("swapkino:events", new[]
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
                        log.LogError(ex, "Outbox event {EventId} failed on attempt {Attempt}", item.Id, item.AttemptCount);
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Outbox processing failed; unacknowledged work will be retried");
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

    private async Task RunImport(SwapKinoDbContext db, ImportJob job, string profileUrl, CancellationToken ct)
    {
        try
        {
            using var response = await http.CreateClient("selenium").PostAsJsonAsync(
                "/api/v1/kinopoisk/ratings",
                new { profile_url = profileUrl, include_unrated = true },
                ct);

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
                var row = new ImportItem
                {
                    ImportJobId = job.Id,
                    KinopoiskUrl = item.KinopoiskUrl,
                    Title = item.Title,
                    Year = item.Year,
                    Genres = item.Genres,
                    Rating = item.Rating,
                    Kind = item.Kind,
                    Page = item.Page,
                };
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
        catch (Exception ex)
        {
            job.Status = "Failed";
            job.Error = ex.Message;
            job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private static async Task ApplyMatches(SwapKinoDbContext db, ImportJob job, IEnumerable<ImportItem> items, CancellationToken ct)
    {
        var movies = await db.Movies.AsNoTracking().ToListAsync(ct);
        foreach (var item in items.Where(x => x.MatchStatus == "pending"))
        {
            var match = movies.FirstOrDefault(movie =>
                string.Equals(Normalize(movie.Title), Normalize(item.Title), StringComparison.OrdinalIgnoreCase) &&
                (item.Year is null || movie.ReleaseDate is null || movie.ReleaseDate.StartsWith(item.Year.Value.ToString(), StringComparison.Ordinal)));

            if (match is null)
            {
                item.MatchStatus = "unmatched";
                item.MatchError = "TMDB-фильм не найден в локальном каталоге";
                continue;
            }

            item.TmdbId = match.TmdbId;
            item.MatchStatus = "matched";
            var actionType = item.Rating is null ? "watched" : "rate";
            var key = $"import:{job.Id}:{item.KinopoiskUrl}";
            if (!await db.UserActions.AnyAsync(x => x.UserId == job.UserId && x.IdempotencyKey == key, ct))
                db.UserActions.Add(new UserAction { UserId = job.UserId, TmdbId = match.TmdbId, ActionType = actionType, Value = item.Rating, IdempotencyKey = key });
        }
        await db.SaveChangesAsync(ct);
    }

    private static string Normalize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private sealed record ImportPayload(Guid JobId, Guid UserId, string ProfileUrl);

    private sealed record RatingsResponse(
        int Total,
        int Rated,
        int Unrated,
        [property: JsonPropertyName("items")] List<RatingItem> Items);

    private sealed record RatingItem(
        string Title,
        int? Year,
        string? Genres,
        double? Rating,
        string Kind,
        [property: JsonPropertyName("kinopoisk_url")] string KinopoiskUrl,
        int Page);
}
