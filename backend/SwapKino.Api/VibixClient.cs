using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SwapKino.Api;

public sealed record VibixEmbed(string PublisherId, string Type, string Id);
public sealed record VibixVideo(string? IframeUrl, string? Name, string? Quality, VibixEmbed? Embed);

/// <summary>
/// Vibix publisher API adapter. Vibix does not resolve TMDB ids directly: its
/// publisher catalog is searched by Kinopoisk/IMDb/title and returns the
/// internal iframe id or the ready-to-use player URL.
/// </summary>
public sealed class VibixClient(HttpClient http, IConfiguration config)
{
    public bool HasExternalId(Movie movie) => movie.TmdbId > 0 || movie.KinopoiskId is not null || !string.IsNullOrWhiteSpace(movie.ImdbId);

    public async Task<VibixVideo?> FindAsync(Movie movie, CancellationToken ct)
    {
        var token = config["VIBIX_API_KEY"];
        if (string.IsNullOrWhiteSpace(token)) return null;

        var searches = new[] { movie.ImdbId, movie.KinopoiskId?.ToString(), movie.Title }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var search in searches)
        {
            var response = await SearchCatalogAsync(search!, token, ct);
            var row = FindRow(response, movie);
            if (row is not null)
            {
                var video = ToVideo(row.Value);
                if (video is not null) return video;
            }
        }

        return null;
    }

    private async Task<JsonDocument?> SearchCatalogAsync(string search, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/publisher/catalog/data");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["draw"] = "1",
            ["start"] = "0",
            ["length"] = "30",
            ["search[value]"] = search,
            ["search[regex]"] = "false"
        });

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) return null;
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        try { return await JsonDocument.ParseAsync(stream, cancellationToken: ct); }
        catch (JsonException) { return null; }
    }

    private static JsonElement? FindRow(JsonDocument? response, Movie movie)
    {
        if (response is null || !response.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
        var rows = data.EnumerateArray().ToArray();
        var imdb = movie.ImdbId?.Trim();
        var kp = movie.KinopoiskId?.ToString();
        var exact = rows.FirstOrDefault(row =>
            (!string.IsNullOrWhiteSpace(imdb) && string.Equals(Text(row, "imdb_id"), imdb, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(kp) && Text(row, "kp_id") == kp));
        if (exact.ValueKind != JsonValueKind.Undefined) return exact;

        var title = Normalize(movie.Title);
        return rows.FirstOrDefault(row =>
            Normalize(Text(row, "name", "name_rus", "name_original")) == title ||
            Normalize(Text(row, "name", "name_rus", "name_original")).Contains(title, StringComparison.Ordinal) ||
            title.Contains(Normalize(Text(row, "name", "name_rus", "name_original")), StringComparison.Ordinal));
    }

    private static VibixVideo? ToVideo(JsonElement row)
    {
        var embedCode = Text(row, "embed_code_new", "embed_code");
        var iframeUrl = Text(row, "iframe_video_url") ?? (Uri.TryCreate(embedCode, UriKind.Absolute, out _) ? embedCode : null);
        var embed = ParseEmbed(embedCode);
        var iframeId = Text(row, "iframe_video_id");
        if (embed is null && !string.IsNullOrWhiteSpace(iframeId))
        {
            var publisherId = Text(row, "publisher_id");
            if (!string.IsNullOrWhiteSpace(publisherId)) embed = new VibixEmbed(publisherId, Text(row, "type") == "serial" ? "series" : "movie", iframeId);
        }
        if (string.IsNullOrWhiteSpace(iframeUrl) && embed is null) return null;
        return new VibixVideo(iframeUrl, Text(row, "name", "name_rus", "name_original"), Text(row, "quality"), embed);
    }

    private static string? Text(JsonElement node, params string[] names)
    {
        foreach (var name in names)
        {
            if (!node.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
            if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) return value.ToString();
        }
        return null;
    }

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static VibixEmbed? ParseEmbed(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(code, "data-(publisher-id|type|id)=[\\\"']([^\\\"']+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            values[match.Groups[1].Value] = match.Groups[2].Value;
        return values.TryGetValue("publisher-id", out var publisherId) && values.TryGetValue("type", out var type) && values.TryGetValue("id", out var id)
            ? new VibixEmbed(publisherId, type.Equals("serial", StringComparison.OrdinalIgnoreCase) ? "series" : type, id)
            : null;
    }
}
