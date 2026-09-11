using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SwapKino.Api;

public sealed record VibixEmbed(string PublisherId, string Type, string Id);
public sealed record VibixVideo(string? IframeUrl, string? Name, string? Quality, VibixEmbed? Embed);
public sealed record VibixLookup(string Status, VibixVideo? Video);

/// <summary>
/// Resolves a Vibix player by an external identifier. IMDb is the primary
/// identifier because SwapKino imports it from TMDB; Kinopoisk is retained
/// solely as a fallback for records that do not have an IMDb id.
/// </summary>
public sealed class VibixClient(HttpClient http, IConfiguration config)
{
    public bool HasExternalId(Movie movie) => movie.KinopoiskId is not null || !string.IsNullOrWhiteSpace(movie.ImdbId);

    public Task<VibixLookup> FindAsync(int? kinopoiskId, string? imdbId, CancellationToken ct)
        => FindByExternalIdsAsync(kinopoiskId, imdbId, ct);

    public async Task<VibixLookup> FindAsync(Movie movie, CancellationToken ct)
        => await FindByExternalIdsAsync(movie.KinopoiskId, movie.ImdbId, ct);

    private async Task<VibixLookup> FindByExternalIdsAsync(int? kinopoiskId, string? imdbId, CancellationToken ct)
    {
        var token = config["VIBIX_API_KEY"];
        if (string.IsNullOrWhiteSpace(token)) return new("not_configured", null);
        if (kinopoiskId is null && string.IsNullOrWhiteSpace(imdbId)) return new("no_external_id", null);

        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            var result = await LookupAsync($"api/v1/publisher/videos/imdb/{Uri.EscapeDataString(imdbId.Trim())}", token, "imdb", imdbId.Trim(), ct);
            if (result.Video is not null || result.Status is "unauthorized" or "forbidden" or "upstream_error") return result;
        }

        if (kinopoiskId is int kpId)
        {
            var result = await LookupAsync($"api/v1/publisher/videos/kp/{kpId}", token, "kp", kpId.ToString(), ct);
            if (result.Video is not null || result.Status is "unauthorized" or "forbidden" or "upstream_error") return result;
        }

        return new("not_published", null);
    }

    private async Task<VibixLookup> LookupAsync(string path, string token, string externalType, string externalId, CancellationToken ct)
    {
        var response = await GetVideoAsync(path, token, ct);
        var video = ToVideo(response.Payload, externalType, externalId);
        return video is null ? new(response.Status, null) : new("available", video);
    }

    private async Task<(JsonDocument? Payload, string Status)> GetVideoAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return (null, "not_found");
        if (response.StatusCode == HttpStatusCode.BadRequest) return (null, "bad_request");
        if (response.StatusCode == HttpStatusCode.Unauthorized) return (null, "unauthorized");
        if (response.StatusCode == HttpStatusCode.Forbidden) return (null, "forbidden");
        if (!response.IsSuccessStatusCode) return (null, "upstream_error");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        try { return (await JsonDocument.ParseAsync(stream, cancellationToken: ct), "ok"); }
        catch (JsonException) { return (null, "upstream_error"); }
    }

    private static VibixVideo? ToVideo(JsonDocument? response, string externalType, string externalId)
    {
        if (response is null) return null;
        var row = response.RootElement;
        var embedCode = Text(row, "embed_code", "embed_code_new");
        var parsedEmbed = ParseEmbed(embedCode);
        var iframeUrl = Text(row, "iframe_url", "iframe_video_url");
        if (!Uri.TryCreate(iframeUrl, UriKind.Absolute, out var parsedUrl) || parsedUrl.Scheme is not ("http" or "https"))
            iframeUrl = null;

        // Vibix's embed_code contains an internal player id which can point to
        // content that is no longer available. Its publisher id is valid, but
        // the SDK must resolve the actual video by IMDb/Kinopoisk id.
        var embed = parsedEmbed is null
            ? null
            : new VibixEmbed(parsedEmbed.PublisherId, externalType, externalId);
        if (string.IsNullOrWhiteSpace(iframeUrl) && embed is null) return null;

        return new VibixVideo(
            iframeUrl,
            Text(row, "name", "name_rus", "name_original"),
            Text(row, "quality"),
            embed);
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

    private static VibixEmbed? ParseEmbed(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(code, "data-(publisher-id|type|id)=[\\\"']([^\\\"']+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            values[match.Groups[1].Value] = match.Groups[2].Value;
        return values.TryGetValue("publisher-id", out var publisherId)
            && values.TryGetValue("type", out var type)
            && values.TryGetValue("id", out var id)
            && !string.IsNullOrWhiteSpace(publisherId)
            && !string.IsNullOrWhiteSpace(type)
            && !string.IsNullOrWhiteSpace(id)
            ? new VibixEmbed(publisherId, type, id)
            : null;
    }
}
