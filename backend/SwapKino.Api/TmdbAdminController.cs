using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SwapKino.Api;

[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "admin")]
public sealed class TmdbAdminController(TmdbClient tmdb) : ControllerBase
{
    [HttpGet("tmdb/genres")]
    public async Task<IActionResult> Genres([FromQuery] bool isSeries = false, CancellationToken ct = default)
    {
        using var json = await tmdb.Get(isSeries ? "/genre/tv/list" : "/genre/movie/list", new() { ["language"] = "ru-RU" }, ct);
        return Ok(json.RootElement.Clone());
    }

    [HttpGet("tmdb/keywords")]
    public async Task<IActionResult> Keywords([FromQuery] string q, CancellationToken ct)
    {
        using var json = await tmdb.Get("/search/keyword", new() { ["query"] = q, ["page"] = "1", ["language"] = "ru-RU" }, ct);
        return Ok(json.RootElement.Clone());
    }

    [HttpGet("tmdb/search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] bool isSeries = false, CancellationToken ct = default)
    {
        using var json = await tmdb.Get(isSeries ? "/search/tv" : "/search/movie", new() { ["query"] = q, ["language"] = "ru-RU", ["page"] = "1" }, ct);
        return Ok(json.RootElement.Clone());
    }

    [HttpGet("tmdb/suggestions")]
    public async Task<IActionResult> Suggestions([FromQuery] string? name, [FromQuery] string? referenceIds, [FromQuery] string? featureIds, [FromQuery] string? genreIds, [FromQuery] bool isSeries = false, CancellationToken ct = default)
    {
        var ids = ParseIds(referenceIds, 5);
        var selectedFeatureIds = ParseIds(featureIds);
        var selectedGenreIds = ParseIds(genreIds);
        var movies = new Dictionary<int, SuggestionMovie>();
        var genres = new Dictionary<int, string>();
        var keywords = new Dictionary<int, string>();
        var referenceGenreIds = new HashSet<int>();
        await LoadReferenceSuggestions(ids, isSeries, movies, genres, keywords, referenceGenreIds, ct);
        var discoverGenres = selectedGenreIds.Count > 0 ? selectedGenreIds : referenceGenreIds;
        await LoadDiscoverSuggestions(discoverGenres, isSeries, movies, ct);
        var localizedMovies = await LocalizeMovies(movies.Values.Where(x => x.OriginalLanguage == "ru" && x.GenreIds.Any(discoverGenres.Contains)).Take(24).ToArray(), isSeries, ct);
        return Ok(new
        {
            movies = localizedMovies.Select(x => new { id = x.Id, name = x.Name, poster_path = x.PosterPath }),
            genres = genres.Where(x => !selectedFeatureIds.Contains(x.Key)).Select(x => new { id = x.Key, name = x.Value }).Take(12),
            keywords = keywords.Where(x => !selectedFeatureIds.Contains(x.Key)).Select(x => new { id = x.Key, name = x.Value }).Take(20)
        });
    }

    private static HashSet<int> ParseIds(string? value, int? take = null)
    {
        var ids = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var id) ? id : 0).Where(x => x > 0).Distinct();
        return (take is int limit ? ids.Take(limit) : ids).ToHashSet();
    }

    private async Task LoadReferenceSuggestions(IEnumerable<int> ids, bool isSeries, Dictionary<int, SuggestionMovie> movies, Dictionary<int, string> genres, Dictionary<int, string> keywords, HashSet<int> referenceGenreIds, CancellationToken ct)
    {
        foreach (var id in ids)
        {
            try
            {
                using var details = await tmdb.Get($"/{(isSeries ? "tv" : "movie")}/{id}", new() { ["language"] = "ru-RU", ["append_to_response"] = "keywords" }, ct);
                ReadTags(details.RootElement, genres, keywords);
                AddReferenceGenres(details.RootElement, referenceGenreIds);
                using var recommendationResponse = await tmdb.Get($"/{(isSeries ? "tv" : "movie")}/{id}/recommendations", new() { ["language"] = "ru-RU", ["page"] = "1" }, ct);
                AddMovies(recommendationResponse.RootElement, isSeries, movies);
            }
            catch (HttpRequestException) { continue; }
        }
    }

    private async Task LoadDiscoverSuggestions(IReadOnlySet<int> genres, bool isSeries, Dictionary<int, SuggestionMovie> movies, CancellationToken ct)
    {
        if (genres.Count == 0) return;
        try
        {
            using var discovered = await tmdb.Get(isSeries ? "/discover/tv" : "/discover/movie", new()
            {
                ["language"] = "ru-RU", ["page"] = "1", ["with_genres"] = string.Join('|', genres),
                ["with_original_language"] = "ru", ["sort_by"] = "popularity.desc", ["vote_count.gte"] = "5"
            }, ct);
            AddMovies(discovered.RootElement, isSeries, movies);
        }
        catch (HttpRequestException) { return; }
    }

    private static void AddReferenceGenres(JsonElement root, HashSet<int> target)
    {
        if (!root.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array) return;
        foreach (var genre in genres.EnumerateArray())
            if (genre.TryGetProperty("id", out var id) && id.TryGetInt32(out var value)) target.Add(value);
    }

    private static void AddMovies(JsonElement root, bool isSeries, Dictionary<int, SuggestionMovie> target)
    {
        if (!root.TryGetProperty("results", out var results)) return;
        foreach (var item in results.EnumerateArray().Take(12))
        {
            var movie = ParseSuggestionMovie(item, isSeries);
            if (movie is not null) target.TryAdd(movie.Id, movie);
        }
    }

    private static SuggestionMovie? ParseSuggestionMovie(JsonElement item, bool isSeries)
    {
        if (!item.TryGetProperty("id", out var idValue) || !idValue.TryGetInt32(out var id)) return null;
        var titleProperty = isSeries ? "name" : "title";
        var title = item.TryGetProperty(titleProperty, out var titleValue) ? titleValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(title)) return null;
        var genreIds = item.TryGetProperty("genre_ids", out var genreValues) && genreValues.ValueKind == JsonValueKind.Array
            ? genreValues.EnumerateArray().Where(x => x.TryGetInt32(out _)).Select(x => x.GetInt32()).ToHashSet() : [];
        var originalLanguage = item.TryGetProperty("original_language", out var language) ? language.GetString() : null;
        var poster = item.TryGetProperty("poster_path", out var posterValue) ? posterValue.GetString() : null;
        return new SuggestionMovie(id, title, poster, genreIds, originalLanguage);
    }

    private async Task<IReadOnlyList<SuggestionMovie>> LocalizeMovies(IReadOnlyList<SuggestionMovie> source, bool isSeries, CancellationToken ct)
    {
        var localized = await Task.WhenAll(source.Select(movie => LocalizeMovie(movie, isSeries, ct)));
        return localized.Where(x => !string.IsNullOrWhiteSpace(x.Name) && Regex.IsMatch(x.Name, "[А-Яа-яЁё]")).ToArray();
    }

    private async Task<SuggestionMovie> LocalizeMovie(SuggestionMovie movie, bool isSeries, CancellationToken ct)
    {
        try
        {
            using var json = await tmdb.Get($"/{(isSeries ? "tv" : "movie")}/{movie.Id}/translations", new(), ct);
            if (!json.RootElement.TryGetProperty("translations", out var translations)) return movie;
            var ru = translations.EnumerateArray().FirstOrDefault(x => string.Equals(x.TryGetProperty("iso_3166_1", out var country) ? country.GetString() : null, "RU", StringComparison.OrdinalIgnoreCase));
            if (ru.ValueKind != JsonValueKind.Object || !ru.TryGetProperty("data", out var data)) return movie;
            var titleProperty = isSeries ? "name" : "title";
            var title = data.TryGetProperty(titleProperty, out var value) ? value.GetString() : null;
            return string.IsNullOrWhiteSpace(title) ? movie : movie with { Name = title };
        }
        catch (HttpRequestException) { return movie; }
    }

    private static void ReadTags(JsonElement root, Dictionary<int, string> genres, Dictionary<int, string> keywords)
    {
        AddNamedTags(root, "genres", genres);
        if (!root.TryGetProperty("keywords", out var keywordContainer)) return;
        AddNamedTags(keywordContainer, "keywords", keywords);
        AddNamedTags(keywordContainer, "results", keywords);
    }

    private static void AddNamedTags(JsonElement root, string property, Dictionary<int, string> target)
    {
        if (!root.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array) return;
        foreach (var item in values.EnumerateArray())
            if (item.TryGetProperty("id", out var id) && id.TryGetInt32(out var key) && item.TryGetProperty("name", out var value)) target.TryAdd(key, value.GetString() ?? "");
    }
}
