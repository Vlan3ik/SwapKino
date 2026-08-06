using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

/// <summary>
/// Один раз после запуска заполняет локальный каталог популярными фильмами.
/// После заполнения обычные запросы читают PostgreSQL и не обращаются к TMDB.
/// </summary>
public sealed class CatalogWarmupService(
    IServiceScopeFactory scopes,
    ILogger<CatalogWarmupService> log,
    IConfiguration config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            var target = Math.Clamp(config.GetValue("CATALOG_WARMUP_PAGES", 5), 1, 20);
            await SyncPopular(target, forceRefresh: false, ct: stoppingToken);

            var intervalHours = Math.Clamp(config.GetValue("CATALOG_SYNC_INTERVAL_HOURS", 6), 1, 168);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SyncPopular(target, forceRefresh: true, ct: stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Недоступность TMDB не должна останавливать API: уже сохранённый
            // локальный каталог продолжает обслуживать пользователей.
            log.LogWarning(ex, "Catalog warmup failed; cached catalog remains available");
        }
    }

    private async Task SyncPopular(int target, bool forceRefresh, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        var count = await db.Movies.CountAsync(ct);
        if (!forceRefresh && count >= target * 20)
        {
            log.LogInformation("Catalog warmup skipped: {Count} movies already cached", count);
            return;
        }

        for (var page = 1; page <= target && !ct.IsCancellationRequested; page++)
        {
            var rows = await tmdb.Discover(page, null, ct, forceRefresh);
            log.LogInformation("Catalog sync page {Page}/{Target}: {Count} movies", page, target, rows.Count);
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }
}
