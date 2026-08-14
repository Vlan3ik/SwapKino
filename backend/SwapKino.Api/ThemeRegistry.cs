namespace SwapKino.Api;

public sealed class ThemeRule
{
    public ThemeRule(string slug, int[] requiredGenres, int[] keywordIds, int[]? excludedGenres = null, bool requiresKeyword = false)
    {
        Slug = slug; RequiredGenres = requiredGenres; KeywordIds = keywordIds;
        ExcludedGenres = excludedGenres ?? []; RequiresKeyword = requiresKeyword;
    }
    public string Slug { get; }
    public int[] RequiredGenres { get; }
    public int[] KeywordIds { get; }
    public int[] ExcludedGenres { get; }
    public bool RequiresKeyword { get; }
}

public sealed record ThemeMembership(string Slug, double Confidence);

public static class ThemeRegistry
{
    public const int Version = 1;

    // IDs are the verified TMDB genre/keyword IDs already used by the catalog.
    // The registry is deliberately local: classification never calls TMDB.
    public static readonly IReadOnlyList<ThemeRule> All =
    [
        new("horror", [27], [12377, 9715, 6152, 11314, 158718, 9725, 131]),
        new("detective", [9648, 80], [9826, 10714, 825, 1423, 10909, 155668]),
        new("psychological", [18, 53, 9648], [362567, 184312, 226106, 9678, 373849, 196767, 4280, 41329], requiresKeyword: true),
        new("crime", [80], [10051, 11105, 3391, 11225, 14903, 10594]),
        new("action", [28], [9716, 14643, 1625, 14512, 10028, 2343]),
        new("survival", [], [1729, 10685, 11931, 7162, 13095, 156121], requiresKeyword: true),
        new("apocalypse", [878, 27, 28], [9715, 1729, 10685, 156121], requiresKeyword: true),
        new("science-fiction", [878], [9951, 9882, 9840, 310, 9714, 10873, 10876]),
        new("space", [878], [9882, 9840, 310, 161176], requiresKeyword: true),
        new("ai-cyberpunk", [878], [10876, 9951, 156121, 10873], requiresKeyword: true),
        new("fantasy", [14], [9882, 10292, 616, 160193, 2343]),
        new("drama", [18], [10084, 10683, 9714, 10497, 15097]),
        new("romance", [10749], [9673, 1310, 10183, 14536, 162718]),
        new("comedy", [35], [6054, 9713, 9715, 10683]),
        new("black-comedy", [35], [157499, 14964, 10683, 9726], requiresKeyword: true),
        new("family", [10751], [10051, 6054, 9713, 179431]),
        new("anime", [16, 14], [10695, 1625, 10028, 9725], requiresKeyword: true),
        new("adult-animation", [16], [157499, 10683, 9726, 10051], requiresKeyword: true),
        new("war", [10752], [1956, 10683, 14903, 10594]),
        new("true-story", [18, 36, 80], [9672, 13027, 10909, 18034], requiresKeyword: true),
        new("western", [37], [6075, 10051, 14819, 1701]),
        new("sport", [], [6075, 333328, 161643, 294708, 190471, 167882, 2006, 8635], requiresKeyword: true),
        new("music", [10402], [10402, 10683, 157499]),
        new("documentary", [99], [])
    ];

    public static IReadOnlyList<ThemeMembership> Classify(Movie movie)
    {
        var genres = movie.MovieGenres.Select(x => x.GenreId).ToHashSet();
        var keywords = movie.MovieKeywords.Select(x => x.KeywordId).ToHashSet();
        return All.Select(rule =>
        {
            var genreMatch = rule.RequiredGenres.Length == 0 || rule.RequiredGenres.Any(genres.Contains);
            var keywordMatches = rule.KeywordIds.Count(keywords.Contains);
            var keywordMatch = !rule.RequiresKeyword || keywordMatches > 0;
            var excluded = rule.ExcludedGenres.Any(genres.Contains);
            if (!genreMatch || !keywordMatch || excluded) return null;
            var confidence = Math.Min(1d, (rule.RequiredGenres.Length == 0 ? 0.45 : 0.65) + Math.Min(.3, keywordMatches * .05));
            return new ThemeMembership(rule.Slug, confidence);
        }).Where(x => x is not null).Select(x => x!).ToArray();
    }

    public static ThemeRule? Find(string slug) => All.FirstOrDefault(x => x.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

    public static string CanonicalSlug(string slug) => slug.ToLowerInvariant() switch
    {
        "comedy-company" or "posmeyatsya" or "teplyi-vecher" => "comedy",
        "animation" or "anime-vecher" or "anime-serial" => "anime",
        "na-odnom-dyhanii" or "nervy-na-predele" or "slomai-mne-mozg" => "psychological",
        "bez-tormozov" or "bolshoe-kino" => "action",
        "ne-smotri-odin" => "horror",
        "temnye-dela" => "detective",
        "v-drugoi-mir" => "fantasy",
        "sredi-zvezd" => "space",
        "buduschee-zdes" => "ai-cyberpunk",
        "konec-sveta" => "apocalypse",
        "vyzhit" => "survival",
        "voennoe-kino" => "war",
        "realnye-sobytiya" or "velikie-lyudi" or "po-sledam-istorii" => "true-story",
        "dikii-zapad" => "western",
        "chernyi-yumor" => "black-comedy",
        "muzyka-gromche" => "music",
        "documentary" => "documentary",
        _ => slug.ToLowerInvariant()
    };
}
