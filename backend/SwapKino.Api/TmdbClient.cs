using System.Net.Http.Headers;
using System.Text.Json;

namespace SwapKino.Api;

public sealed record TmdbPage(IReadOnlyList<Movie> Results, int TotalPages, int TotalResults);
public sealed record TmdbImportCandidate(int TmdbId, bool IsSeries, string Title, string? OriginalTitle, string? ReleaseDate, int VoteCount);

/// Read-only TMDB gateway. Film metadata never crosses this boundary into PostgreSQL.
public sealed class TmdbClient(IHttpClientFactory factory, IConfiguration config)
{
    // Kept only for source compatibility with older test fixtures; db is intentionally unused.
    public TmdbClient(IHttpClientFactory factory, IConfiguration config, SwapKinoDbContext _) : this(factory, config) { }
    public async Task<JsonDocument> Get(string path, Dictionary<string, string?> query, CancellationToken ct)
    {
        var token = config["TMDB_ACCESS_TOKEN"];
        var client = factory.CreateClient("tmdb");
        for (var attempt = 0; attempt <= 3; attempt++)
        {
            var values = new Dictionary<string, string?>(query);
            if (string.IsNullOrWhiteSpace(token)) values["api_key"] = config["TMDB_API_KEY"];
            var uri = path.TrimStart('/') + "?" + string.Join("&", values.Where(x => x.Value is not null).Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!)}"));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            if ((int)response.StatusCode != 429 && (int)response.StatusCode < 500 || attempt == 3) response.EnsureSuccessStatusCode();
            await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct);
        }
        throw new InvalidOperationException("TMDB request retry loop exited unexpectedly.");
    }

    public async Task<TmdbPage> SearchAsync(string query, bool isSeries, CancellationToken ct)
    {
        using var json = await Get(isSeries ? "/search/tv" : "/search/movie", new() { ["language"] = "ru-RU", ["page"] = "1", ["query"] = query, ["include_adult"] = "false" }, ct);
        var results = json.RootElement.GetProperty("results").EnumerateArray().Where(x => x.TryGetProperty("id", out _)).Select(x => Summary(x, isSeries)).ToArray();
        return new(results, Math.Max(1, Integer(json.RootElement, "total_pages") ?? 1), Integer(json.RootElement, "total_results") ?? results.Length);
    }

    public async Task<IReadOnlyList<TmdbImportCandidate>> SearchCandidatesAsync(string query, bool isSeries, CancellationToken ct)
    {
        using var json = await Get(isSeries ? "/search/tv" : "/search/movie", new() { ["language"] = "ru-RU", ["page"] = "1", ["query"] = query, ["include_adult"] = "false" }, ct);
        return json.RootElement.GetProperty("results").EnumerateArray().Where(x => x.TryGetProperty("id", out _)).Select(x => new TmdbImportCandidate(x.GetProperty("id").GetInt32(), isSeries, String(x, isSeries ? "name" : "title") ?? "", String(x, isSeries ? "original_name" : "original_title"), String(x, isSeries ? "first_air_date" : "release_date"), Integer(x, "vote_count") ?? 0)).ToArray();
    }

    public Task<Movie> UpsertSummary(JsonElement item, bool isSeries, CancellationToken ct)
        => Task.FromResult(Summary(item, isSeries));

    public async Task<Movie> Details(int id, CancellationToken ct, bool isSeries = false)
    {
        using var json = await Get($"/{(isSeries ? "tv" : "movie")}/{id}", new() { ["language"] = "ru-RU", ["append_to_response"] = "external_ids,keywords" }, ct);
        return Summary(json.RootElement, isSeries);
    }

    private static Movie Summary(JsonElement x, bool isSeries) => new()
    {
        TmdbId = x.GetProperty("id").GetInt32(), IsSeries = isSeries, Title = String(x, isSeries ? "name" : "title") ?? "",
        OriginalTitle = String(x, isSeries ? "original_name" : "original_title"), Overview = String(x, "overview"), OriginalLanguage = String(x, "original_language"),
        ReleaseDate = String(x, isSeries ? "first_air_date" : "release_date"), VoteAverage = Number(x, "vote_average") ?? 0, VoteCount = Integer(x, "vote_count") ?? 0,
        Popularity = Number(x, "popularity") ?? 0, Adult = Boolean(x, "adult") ?? false, PosterPath = String(x, "poster_path"), BackdropPath = String(x, "backdrop_path"), Payload = x.GetRawText()
    };
    private static string? String(JsonElement x, string key) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Number(JsonElement x, string key) => x.TryGetProperty(key, out var v) && v.TryGetDouble(out var n) ? n : null;
    private static int? Integer(JsonElement x, string key) => x.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : null;
    private static bool? Boolean(JsonElement x, string key) => x.TryGetProperty(key, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;
}
