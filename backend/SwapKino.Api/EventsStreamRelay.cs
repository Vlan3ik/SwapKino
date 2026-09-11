using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace SwapKino.Api;

public sealed class EventsStreamRelay(IConnectionMultiplexer redis, IHubContext<EventsHub> hub, ILogger<EventsStreamRelay> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var stream = redis.GetDatabase();
        // Слушатель получает только новые события. Исторические записи stream не
        // должны повторно отправляться пользователю после перезапуска API.
        var latest = await stream.StreamRangeAsync("swapkino:events", "-", "+", 1, Order.Descending);
        var lastId = latest.Length == 0 ? "0-0" : latest[0].Id.ToString();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await stream.StreamReadAsync("swapkino:events", lastId, 100);
                if (entries.Length == 0) { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); continue; }
                lastId = await RelayEntries(entries, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Redis event relay failed");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task<string> RelayEntries(StreamEntry[] entries, CancellationToken ct)
    {
        var lastId = entries[^1].Id.ToString();
        foreach (var entry in entries)
        {
            lastId = entry.Id.ToString();
            await RelayEntry(entry, ct);
        }
        return lastId;
    }

    private async Task RelayEntry(StreamEntry entry, CancellationToken ct)
    {
        var payload = entry.Values.FirstOrDefault(x => x.Name == "payload").Value;
        if (payload.IsNullOrEmpty) return;
        using var document = JsonDocument.Parse(payload.ToString());
        if (!document.RootElement.TryGetProperty("userId", out var userId)) return;
        var topic = entry.Values.FirstOrDefault(x => x.Name == "topic").Value.ToString();
        await hub.Clients.Group($"user:{userId.GetString()}").SendAsync("event", new { topic, payload = document.RootElement }, ct);
    }
}
