using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SwapKino.Api;

public sealed class RecommendationGateway(IHttpClientFactory factory, IConfiguration config, ILogger<RecommendationGateway> log)
{
    public const string ItemPrefix = "movie:";
    public const string RankerVersion = "gorse-fm-v1";

    private HttpClient Client => factory.CreateClient("gorse");
    private string? ApiKey => config["GORSE_API_KEY"];

    public static string ItemId(int tmdbId, bool isSeries) => $"{ItemPrefix}{(isSeries ? "series" : "movie")}:{tmdbId}";

    public async Task<IReadOnlyList<MovieKey>> GetRecommendationsAsync(Guid userId, string theme, int count, CancellationToken ct)
    {
        var category = string.IsNullOrWhiteSpace(theme) ? null : $"theme:{ThemeRegistry.CanonicalSlug(theme)}";
        var uri = $"api/recommend/{Uri.EscapeDataString(userId.ToString())}?{(category is null ? "" : $"category={Uri.EscapeDataString(category)}&")}n={count}&user-id={Uri.EscapeDataString(userId.ToString())}";
        using var response = await SendAsync(HttpMethod.Get, uri, null, ct);
        if (!response.IsSuccessStatusCode) return [];
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseIds(json.RootElement);
    }

    public async Task<IReadOnlyList<MovieKey>> GetThemePopularAsync(string theme, int count, CancellationToken ct)
    {
        var uri = $"api/latest?category={Uri.EscapeDataString($"theme:{ThemeRegistry.CanonicalSlug(theme)}")}&n={count}";
        using var response = await SendAsync(HttpMethod.Get, uri, null, ct);
        if (!response.IsSuccessStatusCode) return [];
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return ParseIds(json.RootElement);
    }

    public async Task UpsertItemAsync(Movie movie, IReadOnlyList<ThemeMembership> memberships, CancellationToken ct)
    {
        var categories = memberships.Select(x => $"theme:{x.Slug}").Append($"type:{(movie.IsSeries ? "series" : "movie")}").ToArray();
        var labels = new
        {
            genres = movie.MovieGenres.Select(x => $"genre:{x.GenreId}"),
            keywords = movie.MovieKeywords.Select(x => $"keyword:{x.KeywordId}"),
            people = movie.MoviePeople.Where(x => x.Department is "Director" or "Actor").Take(10).Select(x => $"person:{x.PersonId}"),
            language = movie.OriginalLanguage,
            year = int.TryParse(movie.ReleaseDate?.Take(4).ToString(), out var year) ? year : (int?)null
        };
        var item = new { ItemId = ItemId(movie.TmdbId, movie.IsSeries), IsHidden = movie.Adult || memberships.Count == 0, Categories = categories, Labels = labels, Comment = movie.Overview ?? movie.Title, Timestamp = movie.UpdatedAt };
        using var response = await SendAsync(HttpMethod.Post, "api/items", JsonContent.Create(new[] { item }), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendFeedbackAsync(IEnumerable<GorseFeedback> feedback, CancellationToken ct)
    {
        var rows = feedback.ToArray();
        if (rows.Length == 0) return;
        using var response = await SendAsync(HttpMethod.Put, "api/feedback", JsonContent.Create(rows), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "api/health/ready", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, HttpContent? content, CancellationToken ct)
    {
        var body = content is null ? null : await content.ReadAsByteArrayAsync(ct);
        var mediaType = content?.Headers.ContentType?.MediaType;
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            if (body is not null)
            {
                request.Content = new ByteArrayContent(body);
                if (mediaType is not null) request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            }
            if (!string.IsNullOrWhiteSpace(ApiKey)) request.Headers.Add("X-API-Key", ApiKey);
            try
            {
                var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode < 500 || attempt >= 2) return response;
                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < 2) { log.LogDebug(ex, "Gorse request retry {Attempt} for {Uri}", attempt + 1, uri); }
            await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), ct);
        }
    }

    private static IReadOnlyList<MovieKey> ParseIds(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) return [];
        return root.EnumerateArray().Select(x =>
        {
            var id = x.ValueKind == JsonValueKind.String ? x.GetString() : x.TryGetProperty("Id", out var p) ? p.GetString() : null;
            if (id is null || !id.StartsWith(ItemPrefix, StringComparison.Ordinal)) return (MovieKey?)null;
            var parts = id.Split(':');
            return parts.Length == 3 && int.TryParse(parts[2], out var tmdb) ? new MovieKey(tmdb, parts[1] == "series") : null;
        }).Where(x => x is not null).Select(x => x!).Distinct().ToArray();
    }
}

public sealed record GorseFeedback(string FeedbackType, string UserId, string ItemId, double Value, DateTime Timestamp, string? Comment = null);
