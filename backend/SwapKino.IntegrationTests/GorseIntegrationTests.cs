using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace SwapKino.IntegrationTests;

public sealed class GorseIntegrationTests
{
    [Fact]
    public async Task Gorse_accepts_catalog_item_feedback_and_recommendation_request()
    {
        var endpoint = Environment.GetEnvironmentVariable("GORSE_TEST_URL");
        // The compose-backed CI job sets this variable. The normal API test run
        // intentionally omits external services.
        if (string.IsNullOrWhiteSpace(endpoint)) return;

        using var client = new HttpClient { BaseAddress = new Uri(endpoint!) };
        var apiKey = Environment.GetEnvironmentVariable("GORSE_TEST_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var suffix = Guid.NewGuid().ToString("N");
        var itemId = $"integration:movie:{suffix}";
        var userId = $"integration-user:{suffix}";
        try
        {
            var itemResponse = await client.PostAsJsonAsync("api/items", new[] { new
            {
                ItemId = itemId,
                Categories = new[] { "theme:integration" },
                Labels = new { content_tags = new[] { "genre:18", "keyword:integration" }, quality = 8.5 },
                Comment = "integration item",
                Timestamp = DateTime.UtcNow
            }});
            Assert.Equal(HttpStatusCode.OK, itemResponse.StatusCode);

            var feedbackResponse = await client.PutAsJsonAsync("api/feedback", new[] { new
            {
                FeedbackType = "positive", UserId = userId, ItemId = itemId, Value = 1d, Timestamp = DateTime.UtcNow
            }});
            Assert.Equal(HttpStatusCode.OK, feedbackResponse.StatusCode);

            var recommendationResponse = await client.GetAsync($"api/recommend/{Uri.EscapeDataString(userId)}?n=5");
            Assert.Equal(HttpStatusCode.OK, recommendationResponse.StatusCode);
            using var document = JsonDocument.Parse(await recommendationResponse.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        }
        finally
        {
            await client.DeleteAsync($"api/item/{Uri.EscapeDataString(itemId)}");
        }
    }
}
