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
                if (entries.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                {
                    lastId = entry.Id;
                    var payload = entry.Values.FirstOrDefault(x => x.Name == "payload").Value;
                    var topic = entry.Values.FirstOrDefault(x => x.Name == "topic").Value.ToString();
                    if (payload.IsNullOrEmpty) continue;
                    using var document = JsonDocument.Parse(payload.ToString());
                    if (!document.RootElement.TryGetProperty("userId", out var userId)) continue;
                    await hub.Clients.Group($"user:{userId.GetString()}").SendAsync("event", new { topic, payload = document.RootElement }, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                log.LogError(ex, "Redis event relay failed");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}
