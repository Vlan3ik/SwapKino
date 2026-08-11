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

    public async Task<VibixLookup> FindAsync(Movie movie, CancellationToken ct)
    {
        var token = config["VIBIX_API_KEY"];
        if (string.IsNullOrWhiteSpace(token)) return new("not_configured", null);
        if (!HasExternalId(movie)) return new("no_external_id", null);

        var lastStatus = "not_found";

        if (!string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            var imdb = movie.ImdbId.Trim();
            var byImdb = await GetVideoAsync($"api/v1/publisher/videos/imdb/{Uri.EscapeDataString(imdb)}", token, ct);
            lastStatus = byImdb.Status;
            var result = ToVideo(byImdb.Payload, "imdb", imdb);
            if (result is not null) return new("available", result);
            if (byImdb.Status is "unauthorized" or "forbidden" or "upstream_error") return new(byImdb.Status, null);
        }

        if (movie.KinopoiskId is int kpId)
        {
            var byKp = await GetVideoAsync($"api/v1/publisher/videos/kp/{kpId}", token, ct);
            lastStatus = byKp.Status;
            var result = ToVideo(byKp.Payload, "kp", kpId.ToString());
            if (result is not null) return new("available", result);
            if (byKp.Status is "unauthorized" or "forbidden" or "upstream_error") return new(byKp.Status, null);
        }

        return new(lastStatus is "not_found" ? "not_published" : lastStatus, null);
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
