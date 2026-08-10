using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SwapKino.Api;

public sealed record VibixEmbed(string PublisherId, string Type, string Id);
public sealed record VibixVideo(string? IframeUrl, string? Name, string? Quality, VibixEmbed? Embed);

/// <summary>
/// Resolves a Vibix player by a stable external identifier. The publisher
/// catalog is deliberately not used here: title searches can select a
/// different film and the catalog's internal iframe id is not a playback id.
/// </summary>
public sealed class VibixClient(HttpClient http, IConfiguration config)
{
    public bool HasExternalId(Movie movie) => movie.KinopoiskId is not null || !string.IsNullOrWhiteSpace(movie.ImdbId);

    public async Task<VibixVideo?> FindAsync(Movie movie, CancellationToken ct)
    {
        var token = config["VIBIX_API_KEY"];
        if (string.IsNullOrWhiteSpace(token)) return null;

        if (movie.KinopoiskId is int kpId)
        {
            var byKp = await GetVideoAsync($"api/v1/publisher/videos/kp/{kpId}", token, ct);
            var result = ToVideo(byKp, "kp", kpId.ToString());
            if (result is not null) return result;
        }

        if (!string.IsNullOrWhiteSpace(movie.ImdbId))
        {
            var imdb = movie.ImdbId.Trim();
            var byImdb = await GetVideoAsync($"api/v1/publisher/videos/imdb/{Uri.EscapeDataString(imdb)}", token, ct);
            var result = ToVideo(byImdb, "imdb", imdb);
            if (result is not null) return result;
        }

        return null;
    }

    private async Task<JsonDocument?> GetVideoAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        try { return await JsonDocument.ParseAsync(stream, cancellationToken: ct); }
        catch (JsonException) { return null; }
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

        // The documented API can return a ready iframe URL. If it returns
        // only the publisher attributes, use the external-id embed contract;
        // never fall back to the catalog's internal iframe_video_id.
        var embed = parsedEmbed is null
            ? null
            : new VibixEmbed(parsedEmbed.PublisherId, externalType, externalId);
        if (string.IsNullOrWhiteSpace(iframeUrl) && embed is not null)
            iframeUrl = $"https://{embed.PublisherId}.videoframe2.com/embed-{externalType}/{Uri.EscapeDataString(externalId)}";
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
        return values.TryGetValue("publisher-id", out var publisherId) && !string.IsNullOrWhiteSpace(publisherId)
            ? new VibixEmbed(publisherId, "movie", "")
            : null;
    }
}
