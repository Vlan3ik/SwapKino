using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SwapKino.Api;

public static class RecommendationEmbeddings
{
    public const int Dimensions = 384;
    public const string FeatureVersion = "hashed-v1";

    public static float[] Build(Movie movie)
    {
        var vector = new float[Dimensions];
        void Add(string value, float weight)
        {
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            var index = BitConverter.ToUInt32(bytes, 0) % Dimensions;
            var sign = (bytes[4] & 1) == 0 ? 1f : -1f;
            vector[index] += sign * weight;
        }
        foreach (var genre in movie.MovieGenres) Add($"genre:{genre.GenreId}", 3f);
        foreach (var keyword in movie.MovieKeywords) Add($"keyword:{keyword.KeywordId}", 4f);
        foreach (var person in movie.MoviePeople.Where(x => x.Department is "Director" or "Actor").Take(6)) Add($"person:{person.PersonId}:{person.Department}", person.Department == "Director" ? 2f : 1f);
        if (!string.IsNullOrWhiteSpace(movie.OriginalLanguage)) Add($"language:{movie.OriginalLanguage}", .5f);
        if (DateTime.TryParse(movie.ReleaseDate, out var date)) Add($"decade:{date.Year / 10}", .5f);
        if (movie.RuntimeMinutes is > 0) Add($"runtime:{movie.RuntimeMinutes.Value / 30}", .3f);
        var norm = Math.Sqrt(vector.Sum(x => x * x));
        if (norm > 0) for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
        return vector;
    }

    public static float[] Average(IEnumerable<(Movie Movie, double Weight)> movies)
    {
        var result = new float[Dimensions];
        foreach (var (movie, weight) in movies)
        {
            var vector = Build(movie);
            for (var i = 0; i < result.Length; i++) result[i] += vector[i] * (float)weight;
        }
        var norm = Math.Sqrt(result.Sum(x => x * x));
        if (norm > 0) for (var i = 0; i < result.Length; i++) result[i] = (float)(result[i] / norm);
        return result;
    }

    public static float[] FromTasteProfiles(TasteProfileDocument positive, TasteProfileDocument negative)
    {
        var vector = new float[Dimensions];
        void Add(string value, double weight)
        {
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            var index = BitConverter.ToUInt32(bytes, 0) % Dimensions;
            var sign = (bytes[4] & 1) == 0 ? 1f : -1f;
            vector[index] += sign * (float)weight;
        }
        foreach (var item in positive.Genres) Add($"genre:{item.Key}", item.Value * 3);
        foreach (var item in positive.Keywords) Add($"keyword:{item.Key}", item.Value * 4);
        foreach (var item in positive.People) Add($"person:{item.Key}", item.Value);
        foreach (var item in negative.Genres) Add($"genre:{item.Key}", -item.Value * 3);
        foreach (var item in negative.Keywords) Add($"keyword:{item.Key}", -item.Value * 4);
        foreach (var item in negative.People) Add($"person:{item.Key}", -item.Value);
        var norm = Math.Sqrt(vector.Sum(x => x * x));
        if (norm > 0) for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
        return vector;
    }

    public static string Literal(IReadOnlyList<float> vector) => "[" + string.Join(',', vector.Select(x => x.ToString("G9", CultureInfo.InvariantCulture))) + "]";

    public static async Task UpsertAsync(SwapKinoDbContext db, Movie movie, CancellationToken ct)
    {
        var vector = Literal(Build(movie));
        var featureJson = System.Text.Json.JsonSerializer.Serialize(new { movie.TmdbId, movie.IsSeries, FeatureVersion, dimensions = Dimensions });
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""MovieRecommendationFeatures"" (""TmdbId"", ""IsSeries"", ""FeatureJson"", ""FeatureVersion"", ""Embedding"", ""UpdatedAt"")
            VALUES ({movie.TmdbId}, {movie.IsSeries}, {featureJson}, {FeatureVersion}, CAST({vector} AS vector), {DateTime.UtcNow})
            ON CONFLICT (""TmdbId"", ""IsSeries"") DO UPDATE SET ""FeatureJson"" = EXCLUDED.""FeatureJson"", ""FeatureVersion"" = EXCLUDED.""FeatureVersion"", ""Embedding"" = EXCLUDED.""Embedding"", ""UpdatedAt"" = EXCLUDED.""UpdatedAt""", ct);
    }

    public static async Task<List<(int TmdbId, bool IsSeries)>> NearestAsync(SwapKinoDbContext db, IReadOnlyList<float> profile, int limit, CancellationToken ct)
    {
        if (profile.Count != Dimensions || profile.All(x => x == 0)) return [];
        // This connection belongs to EF Core's DbContext. Do not dispose it here:
        // the ranking query continues using the same context immediately after ANN retrieval.
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand($@"
            SELECT ""TmdbId"", ""IsSeries""
            FROM ""MovieRecommendationFeatures""
            WHERE ""Embedding"" IS NOT NULL
            ORDER BY ""Embedding"" <=> CAST(@profile AS vector)
            LIMIT @limit", connection);
        command.Parameters.AddWithValue("profile", Literal(profile));
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<(int, bool)>();
        while (await reader.ReadAsync(ct)) result.Add((reader.GetInt32(0), reader.GetBoolean(1)));
        return result;
    }
}

public sealed class RecommendationFeatureWorker(IServiceScopeFactory scopes, ILogger<RecommendationFeatureWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
                var movies = await db.Movies.AsNoTracking().Include(x => x.MovieGenres).Include(x => x.MovieKeywords).Include(x => x.MoviePeople)
                    .Where(x => (x.PosterPath != null || x.BackdropPath != null) && !db.MovieRecommendationFeatures.Any(feature => feature.TmdbId == x.TmdbId && feature.IsSeries == x.IsSeries))
                    .OrderBy(x => x.TmdbId).Take(20).ToListAsync(stoppingToken);
                foreach (var movie in movies) await RecommendationEmbeddings.UpsertAsync(db, movie, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { log.LogError(ex, "Recommendation feature batch failed"); await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        }
    }
}
