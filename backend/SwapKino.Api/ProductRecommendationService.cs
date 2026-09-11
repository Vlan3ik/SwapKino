using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace SwapKino.Api;

public sealed record TmdbCandidate(int TmdbId, bool IsSeries, double VoteAverage, int VoteCount, double Popularity,
    IReadOnlySet<int> Genres, IReadOnlySet<int> Keywords, string Source);
public sealed record ProductDeckItem(int TmdbId, bool IsSeries);
public sealed record FilmstripPreview(int StrictCount, int RelaxedCount, int ReferenceRecommendations, int ReferenceSimilar, int UniqueCandidates, int DuplicateCount, bool HasCoverReference);
public sealed record DiscoverRequest(bool Series, IEnumerable<int> RequiredGenres, IEnumerable<int> PreferredGenres, IEnumerable<int> RequiredKeywords, IEnumerable<int> ExcludedGenres, IEnumerable<int> ExcludedKeywords, string Source);

/// Owns the complete recommendation pipeline. TMDB supplies candidates only;
/// ranking, filtering and diversity are deterministic SwapKino code.
public sealed class ProductRecommendationService(
    SwapKinoDbContext db, TmdbClient tmdb,
    IDistributedCache cache, ILogger<ProductRecommendationService> log)
{
    private const int DeckSize = 120;
    public async Task<IReadOnlyList<ProductDeckItem>> GetDeckAsync(Guid? userId, string slug, string sessionId, CancellationToken ct)
    {
        var strip = await db.Filmstrips.AsNoTracking().Include(x => x.Features).Include(x => x.References)
            .SingleOrDefaultAsync(x => x.Slug == slug && x.Status == "published", ct) ?? throw new KeyNotFoundException(slug);
        var profile = userId is Guid uid
            ? await db.UserTasteFeatures.AsNoTracking().Where(x => x.UserId == uid).ToListAsync(ct)
            : [];
        var profileVersion = profile.Count == 0 ? "0" : $"{profile.Count}:{profile.Max(x => x.UpdatedAt).Ticks}";
        // Versioned key: older builds could cache an empty finite deck.
        var key = $"recommendation:deck:infinite-v5:{userId?.ToString() ?? "guest"}:{slug}:{profileVersion}:{strip.ConfigVersion}";
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null) return JsonSerializer.Deserialize<List<ProductDeckItem>>(cached) ?? [];

        var requiredGenres = FeatureIds(strip, FilmstripFeatureType.genre, FilmstripFeatureMode.required);
        var preferredGenres = FeatureIds(strip, FilmstripFeatureType.genre, FilmstripFeatureMode.preferred);
        var excludedGenres = FeatureIds(strip, FilmstripFeatureType.genre, FilmstripFeatureMode.excluded);
        var requiredKeywords = FeatureIds(strip, FilmstripFeatureType.keyword, FilmstripFeatureMode.required);
        var excludedKeywords = FeatureIds(strip, FilmstripFeatureType.keyword, FilmstripFeatureMode.excluded);
        var sources = new List<TmdbCandidate>();
        // Independent retrieval sources: personalized discover, plain discover,
        // one-hop reference recommendations, and one-hop reference similar.
        var tasteGenres = profile.Where(x => x.FeatureType == FilmstripFeatureType.genre && x.Weight > 0)
            .OrderByDescending(x => x.Weight * x.Confidence).Take(3).Select(x => x.TmdbFeatureId);
        await AddSafe(sources, () => Discover(new DiscoverRequest(strip.IsSeries, requiredGenres, preferredGenres.Concat(tasteGenres).Distinct(), requiredKeywords, excludedGenres, excludedKeywords, "personalized"), ct));
        await AddSafe(sources, () => Discover(new DiscoverRequest(strip.IsSeries, requiredGenres, preferredGenres, requiredKeywords, excludedGenres, excludedKeywords, "discover"), ct));
        foreach (var reference in strip.References.OrderByDescending(x => x.Weight).Take(12))
        {
            await AddSafe(sources, () => Similarity(reference, "recommendations", ct));
            await AddSafe(sources, () => Similarity(reference, "similar", ct));
        }
        var excluded = userId is Guid u ? await Excluded(u, ct) : [];
        var recent = userId is Guid ru ? (await db.RecommendationImpressions.AsNoTracking().Where(x => x.UserId == ru && x.ShownAt > DateTime.UtcNow.AddDays(-7)).Select(x => new ProductDeckItem(x.TmdbId, x.IsSeries)).ToListAsync(ct)).ToHashSet() : [];
        var ranked = sources.Where(x => MatchesStrip(x, requiredGenres, preferredGenres, excludedGenres))
            .GroupBy(x => new ProductDeckItem(x.TmdbId, x.IsSeries)).Select(g => Rank(g.First(), g.Count(), strip, profile))
            .Where(x => !excluded.Contains(x.Item) && !recent.Contains(x.Item))
            .OrderByDescending(x => x.Score).ToList();

        // A filmstrip must never become empty just because a user has already
        // seen the whole currently available TMDB result set. Keep the normal
        // exclusions first, then progressively relax only the exhausted part.
        if (ranked.Count == 0)
        {
            ranked = sources.Where(x => MatchesStrip(x, requiredGenres, preferredGenres, excludedGenres))
                .GroupBy(x => new ProductDeckItem(x.TmdbId, x.IsSeries)).Select(g => Rank(g.First(), g.Count(), strip, profile))
                .OrderByDescending(x => x.Score).ToList();
        }
        if (ranked.Count == 0)
        {
            ranked = sources.Where(x => MatchesStrip(x, [], [], excludedGenres)).GroupBy(x => new ProductDeckItem(x.TmdbId, x.IsSeries))
                .Select(g => Rank(g.First(), g.Count(), strip, profile))
                .OrderByDescending(x => x.Score).ToList();
        }
        var diverse = DiversityReranker(ranked, DeckSize);
        // References are the editor's semantic anchors and lead the deck.
        var referenceItems = strip.References.OrderByDescending(x => x.Weight)
            .Select(x => new ProductDeckItem(x.TmdbId, x.IsSeries));
        diverse = referenceItems.Concat(diverse).Distinct().Take(DeckSize).ToList();
        if (diverse.Count == 0)
        {
            // Last-resort seed: references are valid TMDB films and allow the
            // UI to keep working even when TMDB retrieval is temporarily empty.
            diverse = strip.References.OrderByDescending(x => x.Weight)
                .Select(x => new ProductDeckItem(x.TmdbId, x.IsSeries)).Distinct().ToList();
        }
        await cache.SetStringAsync(key, JsonSerializer.Serialize(diverse), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30) }, ct);
        log.LogInformation("product recommendation deck slug={Slug} user={UserId} candidates={Candidates} deck={Deck}", slug, userId, sources.Count, diverse.Count);
        return diverse;
    }

    public async Task<FilmstripPreview> PreviewAsync(Guid filmstripId, CancellationToken ct)
    {
        var strip = await db.Filmstrips.AsNoTracking().Include(x => x.Features).Include(x => x.References).SingleOrDefaultAsync(x => x.Id == filmstripId, ct) ?? throw new KeyNotFoundException(filmstripId.ToString());
        var requiredGenres = FeatureIds(strip, FilmstripFeatureType.genre, FilmstripFeatureMode.required);
        var preferredGenres = FeatureIds(strip, FilmstripFeatureType.genre, FilmstripFeatureMode.preferred);
        var excludedGenres = FeatureIds(strip, FilmstripFeatureType.genre, FilmstripFeatureMode.excluded);
        var requiredKeywords = FeatureIds(strip, FilmstripFeatureType.keyword, FilmstripFeatureMode.required);
        var excludedKeywords = FeatureIds(strip, FilmstripFeatureType.keyword, FilmstripFeatureMode.excluded);
        var strict = await Discover(new DiscoverRequest(strip.IsSeries, requiredGenres, [], requiredKeywords, excludedGenres, excludedKeywords, "preview-strict"), ct);
        var relaxed = await Discover(new DiscoverRequest(strip.IsSeries, requiredGenres, preferredGenres, requiredKeywords, excludedGenres, excludedKeywords, "preview-relaxed"), ct);
        var references = new List<TmdbCandidate>();
        foreach (var reference in strip.References) { await AddSafe(references, () => Similarity(reference, "recommendations", ct)); await AddSafe(references, () => Similarity(reference, "similar", ct)); }
        var all = strict.Concat(relaxed).Concat(references).ToList(); var unique = all.Select(x => new ProductDeckItem(x.TmdbId, x.IsSeries)).Distinct().Count();
        return new(strict.Count, relaxed.Count, references.Count(x => x.Source == "recommendations"), references.Count(x => x.Source == "similar"), unique, all.Count - unique, strip.References.Count > 0);
    }

    private async Task<IReadOnlyList<TmdbCandidate>> Discover(DiscoverRequest request, CancellationToken ct)
    {
        var (series, requiredGenres, preferredGenres, requiredKeywords, excludedGenres, excludedKeywords, source) = request;
        var required = requiredGenres.Distinct().ToArray();
        var preferred = preferredGenres.Distinct().ToArray();
        // TMDB treats comma-separated genres as AND and pipe-separated genres
        // as OR. Required genres stay strict; preferred genres constrain the
        // retrieval when there are no required genres instead of being ignored.
        var genreQuery = required.Length > 0 ? string.Join(',', required) : JoinOr(preferred);
        using var json = await tmdb.Get(series ? "/discover/tv" : "/discover/movie", new() {
            ["language"] = "ru-RU", ["page"] = "1", ["sort_by"] = "popularity.desc", ["include_adult"] = "false",
            ["with_genres"] = genreQuery, ["with_keywords"] = Join(requiredKeywords),
            ["without_genres"] = Join(excludedGenres), ["without_keywords"] = Join(excludedKeywords), ["vote_count.gte"] = "5"
        }, ct);
        return Parse(json.RootElement, series, source).ToArray();
    }
    private async Task<IReadOnlyList<TmdbCandidate>> Similarity(FilmstripReference reference, string endpoint, CancellationToken ct)
    {
        using var json = await tmdb.Get($"/{(reference.IsSeries ? "tv" : "movie")}/{reference.TmdbId}/{endpoint}", new() { ["language"] = "ru-RU", ["page"] = "1" }, ct);
        return Parse(json.RootElement, reference.IsSeries, endpoint).ToArray();
    }
    private async Task AddSafe(List<TmdbCandidate> target, Func<Task<IReadOnlyList<TmdbCandidate>>> source)
    {
        try { target.AddRange(await source()); }
        catch (HttpRequestException ex) { log.LogWarning(ex, "TMDB recommendation source failed"); }
        catch (TaskCanceledException) { log.LogWarning("TMDB recommendation source timed out"); }
    }
    private static IEnumerable<TmdbCandidate> Parse(JsonElement root, bool series, string source)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) yield break;
        foreach (var x in results.EnumerateArray()) if (x.TryGetProperty("id", out var id))
        {
            var genres = x.TryGetProperty("genre_ids", out var gs) && gs.ValueKind == JsonValueKind.Array ? gs.EnumerateArray().Where(v => v.TryGetInt32(out _)).Select(v => v.GetInt32()).ToHashSet() : new HashSet<int>();
            yield return new(id.GetInt32(), series, Number(x, "vote_average"), Integer(x, "vote_count"), Number(x, "popularity"), genres, new HashSet<int>(), source);
        }
    }
    private static bool MatchesStrip(TmdbCandidate candidate, IEnumerable<int> required, IEnumerable<int> preferred, IEnumerable<int> excluded)
    {
        var requiredIds = required.ToArray();
        var preferredIds = preferred.ToArray();
        return requiredIds.All(candidate.Genres.Contains)
            && (preferredIds.Length == 0 || preferredIds.Any(candidate.Genres.Contains))
            && !excluded.Any(candidate.Genres.Contains);
    }
    private static (ProductDeckItem Item, double Score, IReadOnlySet<int> Genres) Rank(TmdbCandidate c, int sourceCount, Filmstrip strip, IReadOnlyList<UserTasteFeature> profile)
    {
        var sourceBonus = c.Source switch
        {
            "recommendations" => 1.2,
            "similar" => 1.0,
            "personalized" => .35,
            _ => 0
        };
        var score = sourceBonus + Math.Log(1 + sourceCount) * .8 + Math.Log(1 + c.Popularity) * .04 + c.VoteAverage * .1;
        foreach (var feature in strip.Features.Where(x => x.FeatureType == FilmstripFeatureType.genre))
        {
            if (!c.Genres.Contains(feature.TmdbFeatureId)) continue;
            score += feature.Mode switch
            {
                FilmstripFeatureMode.preferred => feature.Weight * 1.5,
                FilmstripFeatureMode.required => feature.Weight,
                _ => -feature.Weight * 3
            };
        }
        foreach (var feature in profile.Where(x => x.FeatureType == FilmstripFeatureType.genre))
        {
            if (c.Genres.Contains(feature.TmdbFeatureId)) score += feature.Weight * feature.Confidence;
        }
        return (new ProductDeckItem(c.TmdbId, c.IsSeries), score, c.Genres);
    }
    private static IReadOnlyList<ProductDeckItem> DiversityReranker(IEnumerable<(ProductDeckItem Item, double Score, IReadOnlySet<int> Genres)> ranked, int count)
    {
        var pool = ranked.ToList(); var result = new List<ProductDeckItem>(); var used = new HashSet<int>();
        while (result.Count < count && pool.Count > 0)
        {
            var next = pool.OrderByDescending(x => x.Score - x.Genres.Count(g => used.Contains(g)) * .18).First();
            result.Add(next.Item);
            foreach (var genre in next.Genres) used.Add(genre);
            pool.Remove(next);
        }
        return result;
    }
    private async Task<HashSet<ProductDeckItem>> Excluded(Guid userId, CancellationToken ct) => (await db.UserMovieStates.AsNoTracking().Where(x => x.UserId == userId && (x.Rating != null || x.Watched || x.SuppressedUntil > DateTime.UtcNow)).Select(x => new ProductDeckItem(x.TmdbId, x.IsSeries)).ToListAsync(ct)).ToHashSet();
    private static int[] FeatureIds(Filmstrip f, FilmstripFeatureType type, FilmstripFeatureMode mode) => f.Features.Where(x => x.FeatureType == type && x.Mode == mode).Select(x => x.TmdbFeatureId).ToArray();
    private static string? Join(IEnumerable<int> ids) { var a = ids.Distinct().ToArray(); return a.Length == 0 ? null : string.Join(',', a); }
    private static string? JoinOr(IEnumerable<int> ids) { var a = ids.Distinct().ToArray(); return a.Length == 0 ? null : string.Join('|', a); }
    private static double Number(JsonElement x, string k) => x.TryGetProperty(k, out var v) && v.TryGetDouble(out var n) ? n : 0;
    private static int Integer(JsonElement x, string k) => x.TryGetProperty(k, out var v) && v.TryGetInt32(out var n) ? n : 0;
}
