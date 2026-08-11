using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

/// <summary>
/// Постепенно расширяет локальный каталог. Один проход обрабатывает только по
/// одной странице каждого источника, поэтому перезапуск не сбрасывает прогресс
/// и не создаёт резкую нагрузку на TMDB.
/// </summary>
public sealed class CatalogWarmupService(
    IServiceScopeFactory scopes,
    ILogger<CatalogWarmupService> log,
    IConfiguration config) : BackgroundService
{
    private static readonly SourceDefinition[] Sources =
    [
        new("popular", false, "/discover/movie"),
        new("top-rated", false, "/movie/top_rated"),
        new("now-playing", false, "/movie/now_playing"),
        new("upcoming", false, "/movie/upcoming"),
        new("tv-popular", true, "/discover/tv"),
        new("tv-top-rated", true, "/tv/top_rated"),
        new("tv-airing", true, "/tv/airing_today"),
        new("tv-on-air", true, "/tv/on_the_air")
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("CATALOG_SYNC_ENABLED", true))
        {
            log.LogInformation("Catalog sync is disabled by CATALOG_SYNC_ENABLED=false");
            return;
        }
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            var intervalSeconds = Math.Clamp(config.GetValue("CATALOG_SYNC_INTERVAL_SECONDS", 12), 3, 3600);
            var pagesPerCycle = Math.Clamp(config.GetValue("CATALOG_PAGES_PER_CYCLE", 1), 1, 4);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

            do
            {
                await SyncBatch(pagesPerCycle, stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Catalog sync stopped; the next container restart can resume from saved cursors");
        }
    }

    private async Task SyncBatch(int pagesPerSource, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();

        foreach (var source in Sources)
        {
            for (var i = 0; i < pagesPerSource && !ct.IsCancellationRequested; i++)
            {
                var state = await db.CatalogSyncStates.SingleOrDefaultAsync(x => x.Source == source.Name && x.IsSeries == source.IsSeries, ct);
                if (state is null)
                {
                    state = new CatalogSyncState { Source = source.Name, IsSeries = source.IsSeries, NextPage = 1 };
                    db.CatalogSyncStates.Add(state);
                }

                var maxPage = state.TotalPages is > 0 ? Math.Min(state.TotalPages.Value, 500) : 500;
                if (state.NextPage > maxPage)
                {
                    // После полного прохода начинаем новый цикл: TMDB мог обновить
                    // рейтинги и состав выдачи, а уникальные записи останутся в БД.
                    state.NextPage = 1;
                    state.TotalPages = null;
                }

                try
                {
                    var page = state.NextPage;
                    var result = await tmdb.DiscoverPage(page, null, ct, forceRefresh: true, endpoint: source.Endpoint);
                    state.TotalPages = Math.Clamp(result.TotalPages, 1, 500);
                    state.NextPage = page >= state.TotalPages.Value ? state.TotalPages.Value + 1 : page + 1;
                    state.ImportedCount += result.Results.Count;
                    state.LastFetchedAt = DateTime.UtcNow;
                    state.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    log.LogInformation("Catalog sync {Source} page {Page}/{Total}: {Count} results, cursor {NextPage}", source.Name, page, state.TotalPages, result.Results.Count, state.NextPage);
                }
                catch (HttpRequestException ex)
                {
                    log.LogWarning(ex, "Catalog source {Source} failed at page {Page}; cursor preserved", source.Name, state.NextPage);
                    await db.SaveChangesAsync(ct);
                    break;
                }

                // Дополнительная пауза между страницами защищает от 429 и не
                // мешает enrichment-сервису обрабатывать детали.
                await Task.Delay(TimeSpan.FromMilliseconds(700), ct);
            }
        }
    }

    private sealed record SourceDefinition(string Name, bool IsSeries, string Endpoint);
}
