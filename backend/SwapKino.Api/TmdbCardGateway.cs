using System.Text.Json;
using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;

namespace SwapKino.Api;

public sealed class TmdbCardGateway(TmdbClient tmdb, IDistributedCache cache)
{
    public async Task<(IReadOnlyList<JsonElement> Results, int Total)> ListAsync(TmdbListRequest request, CancellationToken ct)
    {
        var (path, parameters) = BuildListRequest(request);
        using var json = await tmdb.Get(path, parameters, ct);
        var results = ReadResults(json.RootElement);
        var total = ReadTotal(json.RootElement, results.Length);
        return (results, total);
    }

    private static (string Path, Dictionary<string, string?> Parameters) BuildListRequest(TmdbListRequest request)
    {
        var (isSeries, query, page, genres, minRating, yearFrom, yearTo, sort) = request;
        var discover = string.IsNullOrWhiteSpace(query);
        var media = isSeries ? "tv" : "movie";
        var parameters = new Dictionary<string, string?> { ["language"] = "ru-RU", ["page"] = Math.Clamp(page, 1, 500).ToString(), ["query"] = discover ? null : query, ["include_adult"] = "false" };
        if (!discover) return ($"/search/{media}", parameters);

        var datePrefix = isSeries ? "first_air_date" : "primary_release_date";
        parameters["sort_by"] = sort switch
        {
            "rating" => "vote_average.desc",
            "newest" => $"{datePrefix}.desc",
            "oldest" => $"{datePrefix}.asc",
            _ => "popularity.desc"
        };
        parameters["with_genres"] = genres;
        parameters["vote_average.gte"] = minRating?.ToString(CultureInfo.InvariantCulture);
        parameters[$"{datePrefix}.gte"] = yearFrom is null ? null : $"{yearFrom}-01-01";
        parameters[$"{datePrefix}.lte"] = yearTo is null ? null : $"{yearTo}-12-31";
        return ($"/discover/{media}", parameters);
    }

    private static JsonElement[] ReadResults(JsonElement root) => root.TryGetProperty("results", out var rows) && rows.ValueKind == JsonValueKind.Array
        ? rows.EnumerateArray().Select(x => x.Clone()).ToArray()
        : [];

    private static int ReadTotal(JsonElement root, int fallback) => root.TryGetProperty("total_results", out var count) && count.TryGetInt32(out var value) ? value : fallback;
    public async Task<JsonElement> GetAsync(ProductDeckItem item, CancellationToken ct)
    {
        var key = $"tmdb:card:{(item.IsSeries ? "tv" : "movie")}:{item.TmdbId}:ru-RU:v1";
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null) using (var document = JsonDocument.Parse(cached)) return document.RootElement.Clone();
        using var response = await tmdb.Get($"/{(item.IsSeries ? "tv" : "movie")}/{item.TmdbId}", new() {
            ["language"] = "ru-RU", ["append_to_response"] = "credits,videos,watch/providers,images,external_ids", ["include_image_language"] = "ru,en,null"
        }, ct);
        var raw = response.RootElement.GetRawText();
        await cache.SetStringAsync(key, raw, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6) }, ct);
        return response.RootElement.Clone();
    }

    public static object Card(JsonElement x, ProductDeckItem item)
    {
        var title = Text(x, item.IsSeries ? "name" : "title") ?? $"TMDB {item.TmdbId}";
        var release = Text(x, item.IsSeries ? "first_air_date" : "release_date");
        var genres = x.TryGetProperty("genres", out var gs) && gs.ValueKind == JsonValueKind.Array
            ? gs.EnumerateArray().Where(g => g.TryGetProperty("id", out _)).Select(g => new { id = g.GetProperty("id").GetInt32(), name = Text(g, "name") ?? "" }).ToArray()
            : Array.Empty<object>();
        var gallery = x.TryGetProperty("images", out var imageSet) && imageSet.ValueKind == JsonValueKind.Object
            ? new
            {
                backdrops = ImagePaths(imageSet, "backdrops", "w780"),
                posters = ImagePaths(imageSet, "posters", "w500")
            }
            : new { backdrops = Array.Empty<string>(), posters = Array.Empty<string>() };
        return new { id = item.TmdbId, tmdbId = item.TmdbId, isSeries = item.IsSeries, title, originalTitle = Text(x, item.IsSeries ? "original_name" : "original_title"), overview = Text(x, "overview"), releaseDate = release, runtime = item.IsSeries ? (int?)null : Integer(x, "runtime"), rating = Number(x, "vote_average"), voteCount = Integer(x, "vote_count"), genres, posterUrl = Image(Text(x, "poster_path"), "w500"), backdropUrl = Image(Text(x, "backdrop_path"), "w780"), images = gallery.backdrops, posters = gallery.posters, detailsState = "ready" };
    }
    public static string? PosterUrl(JsonElement x) => Image(Text(x, "poster_path"), "w500");
    private static string? Text(JsonElement x, string key) => x.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Integer(JsonElement x, string key) => x.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : null;
    private static double Number(JsonElement x, string key) => x.TryGetProperty(key, out var v) && v.TryGetDouble(out var n) ? n : 0;
    private static string? Image(string? path, string size) => string.IsNullOrWhiteSpace(path) ? null : $"https://image.tmdb.org/t/p/{size}{path}";
    private static string[] ImagePaths(JsonElement imageSet, string key, string size) =>
        imageSet.TryGetProperty(key, out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray().Select(row => Text(row, "file_path")).Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Image(path, size)!).Distinct().ToArray()
            : [];
}

public sealed record TmdbListRequest(bool IsSeries, string? Query, int Page, string? Genres, double? MinRating, int? YearFrom, int? YearTo, string Sort);
