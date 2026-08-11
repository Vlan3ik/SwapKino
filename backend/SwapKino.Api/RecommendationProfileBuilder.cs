using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

public sealed record TasteProfileDocument(
    Dictionary<string, double> Genres,
    Dictionary<string, double> Keywords,
    Dictionary<string, double> People,
    int SignalCount);

public static class RecommendationProfileBuilder
{
    public const string ModelVersion = "taste-v1";
    public const string ProfileCachePrefix = "rec:user:";

    public static async Task<UserTasteProfile> BuildAsync(SwapKinoDbContext db, Guid userId, CancellationToken ct)
    {
        var actions = await db.UserActions.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        var ids = actions.Select(x => (x.TmdbId, x.IsSeries)).Distinct().ToArray();
        var tmdbIds = ids.Select(x => x.TmdbId).ToArray();
        var movies = await db.Movies.AsNoTracking()
            .Include(x => x.MovieGenres).Include(x => x.MovieKeywords).Include(x => x.MoviePeople)
            .Where(x => tmdbIds.Contains(x.TmdbId)).ToListAsync(ct);
        var weights = actions.GroupBy(x => (x.TmdbId, x.IsSeries)).ToDictionary(
            x => x.Key, x => x.Sum(a => Weight(a.ActionType, a.Value) * Decay(a.CreatedAt, a.ActionType is not ("rate" or "rating" or "rate_inline"))));
        var positive = new TasteProfileDocument([], [], [], actions.Count);
        var negative = new TasteProfileDocument([], [], [], actions.Count);
        foreach (var movie in movies)
        {
            var weight = weights.GetValueOrDefault((movie.TmdbId, movie.IsSeries));
            if (weight == 0) continue;
            var target = weight > 0 ? positive : negative;
            var magnitude = Math.Abs(weight);
            foreach (var genre in movie.MovieGenres) Add(target.Genres, genre.GenreId.ToString(), magnitude / Math.Max(1, movie.MovieGenres.Count));
            foreach (var keyword in movie.MovieKeywords) Add(target.Keywords, keyword.KeywordId.ToString(), magnitude / Math.Sqrt(Math.Max(1, movie.MovieKeywords.Count)));
            foreach (var person in movie.MoviePeople.Where(x => x.Department is "Director" or "Actor").Take(6)) Add(target.People, person.PersonId.ToString(), magnitude / (person.Department == "Director" ? 1 : 5));
        }
        var current = await db.UserTasteProfiles.FindAsync([userId], ct) ?? new UserTasteProfile { UserId = userId };
        current.PositiveProfileJson = JsonSerializer.Serialize(positive);
        current.NegativeProfileJson = JsonSerializer.Serialize(negative);
        current.PositiveEmbeddingJson = JsonSerializer.Serialize(RecommendationEmbeddings.FromTasteProfiles(positive, new([], [], [], 0)));
        current.NegativeEmbeddingJson = JsonSerializer.Serialize(RecommendationEmbeddings.FromTasteProfiles(new([], [], [], 0), negative));
        current.ProfileVersion++;
        current.ModelVersion = ModelVersion;
        current.UpdatedAt = DateTime.UtcNow;
        if (db.Entry(current).State == EntityState.Detached) db.UserTasteProfiles.Add(current);
        return current;
    }

    private static void Add(Dictionary<string, double> target, string key, double value) => target[key] = target.GetValueOrDefault(key) + value;
    private static double Weight(string action, double? value) => action switch
    {
        "favorite" => 2,
        "swipe_right" => 1,
        "more_like_this" => 2,
        "less_like_this" => -2,
        "not_for_me" => -3,
        "not_interested" => -3,
        "rate" or "rating" or "rate_inline" => value is >= 9 ? 3 : value is 8 ? 2 : value is 7 ? 1 : value is <= 5 ? -2 : 0,
        _ => 0
    };
    private static double Decay(DateTime at, bool apply) => apply ? Math.Exp(-Math.Max(0, (DateTime.UtcNow - at).TotalDays) / 180d) : 1;
}
