using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SwapKino.Api;

public sealed record VibixEmbed(string PublisherId, string Type, string Id);
public sealed record VibixVideo(string? IframeUrl, string? Name, string? Quality, VibixEmbed? Embed);

public sealed class VibixClient(HttpClient http, IConfiguration config)
{
    public bool HasExternalId(Movie movie) => !string.IsNullOrWhiteSpace(FindExternalId(movie).Id);

    public async Task<VibixVideo?> FindAsync(Movie movie, CancellationToken ct)
    {
        var token = config["VIBIX_API_KEY"];
        if (string.IsNullOrWhiteSpace(token)) return null;
        var (kind, id) = FindExternalId(movie);
        if (string.IsNullOrWhiteSpace(id)) return null;
        var path = kind == "imdb" ? $"api/v1/publisher/videos/imdb/{Uri.EscapeDataString(id)}" : $"api/v1/publisher/videos/kp/{Uri.EscapeDataString(id)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = json.RootElement;
        var embedCode = String(root, "embed_code");
        return new VibixVideo(String(root, "iframe_url"), String(root, "name"), String(root, "quality"), ParseEmbed(embedCode));
    }

    private static (string Kind, string? Id) FindExternalId(Movie movie)
    {
        try
        {
            using var document = JsonDocument.Parse(movie.Payload);
            var root = document.RootElement;
            if (root.TryGetProperty("external_ids", out var external))
            {
                var imdb = String(external, "imdb_id");
                if (!string.IsNullOrWhiteSpace(imdb)) return ("imdb", imdb);
                var kp = String(external, "kinopoisk_id") ?? String(external, "kp_id");
                if (!string.IsNullOrWhiteSpace(kp)) return ("kp", kp);
            }
            var payloadImdb = String(root, "imdb_id");
            if (!string.IsNullOrWhiteSpace(payloadImdb)) return ("imdb", payloadImdb);
        }
        catch (JsonException) { }
        return ("", null);
    }

    private static string? String(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static VibixEmbed? ParseEmbed(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(code, "data-(publisher-id|type|id)=[\\\"']([^\\\"']+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            values[match.Groups[1].Value] = match.Groups[2].Value;
        return values.TryGetValue("publisher-id", out var publisherId) && values.TryGetValue("type", out var type) && values.TryGetValue("id", out var id) ? new VibixEmbed(publisherId, type, id) : null;
    }
}
