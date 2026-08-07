using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

public sealed class TmdbClient(IHttpClientFactory factory, IConfiguration config, SwapKinoDbContext db)
{
    public async Task<JsonDocument> Get(string path, Dictionary<string, string?> query, CancellationToken ct)
    {
        var relativePath = path.TrimStart('/');
        var token = config["TMDB_ACCESS_TOKEN"];
        var client = factory.CreateClient("tmdb");
        for (var attempt = 0; ; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, relativePath + "?" + string.Join("&", query.Where(x => x.Value is not null).Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!)}")));
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            else request.RequestUri = new Uri(request.RequestUri + $"&api_key={config["TMDB_API_KEY"]}", UriKind.Relative);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

            var retryable = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
            if (!retryable || attempt >= 3)
            {
                response.EnsureSuccessStatusCode();
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, 8000)), ct);
        }
    }

    public async Task<List<Movie>> Discover(int page, string? search, CancellationToken ct, bool forceRefresh = false)
    {
        if (!forceRefresh && string.IsNullOrWhiteSpace(search))
        {
            var cached = await db.Movies
                .AsNoTracking()
                .OrderByDescending(x => x.Popularity)
                .Skip(Math.Max(0, page - 1) * 20)
                .Take(20)
                .ToListAsync(ct);
            if (cached.Count == 20) return cached;
        }

        try
        {
            var json = await Get(search is null ? "/discover/movie" : "/search/movie", new() { ["language"] = "ru-RU", ["page"] = page.ToString(), ["query"] = search, ["include_adult"] = "false", ["sort_by"] = "popularity.desc" }, ct);
            var result = new List<Movie>();
            foreach (var x in json.RootElement.GetProperty("results").EnumerateArray())
            {
                var id = x.GetProperty("id").GetInt32();
                var movie = await db.Movies.FindAsync([id], ct) ?? new Movie { TmdbId = id };
                movie.Title = x.TryGetProperty("title", out var title) ? title.GetString() ?? "" : movie.Title;
                movie.OriginalTitle = x.TryGetProperty("original_title", out var original) ? original.GetString() : movie.OriginalTitle;
                movie.Overview = x.TryGetProperty("overview", out var overview) ? overview.GetString() : movie.Overview;
                movie.ReleaseDate = x.TryGetProperty("release_date", out var date) ? date.GetString() : movie.ReleaseDate;
                movie.VoteAverage = x.TryGetProperty("vote_average", out var rating) ? rating.GetDouble() : movie.VoteAverage;
                movie.VoteCount = x.TryGetProperty("vote_count", out var votes) ? votes.GetInt32() : movie.VoteCount;
                movie.Popularity = x.TryGetProperty("popularity", out var pop) ? pop.GetDouble() : movie.Popularity;
                movie.PosterPath = x.TryGetProperty("poster_path", out var poster) ? poster.GetString() : movie.PosterPath;
                movie.BackdropPath = x.TryGetProperty("backdrop_path", out var back) ? back.GetString() : movie.BackdropPath;
                movie.Payload = x.GetRawText();
                if (db.Entry(movie).State == EntityState.Detached) db.Movies.Add(movie);
                result.Add(movie);
            }
            await db.SaveChangesAsync(ct);
            return result;
        }
        catch (HttpRequestException)
        {
            return await Fallback(search, page, ct);
        }
    }

    public async Task<Movie> Details(int id, CancellationToken ct)
    {
        try
        {
            var json = await Get($"/movie/{id}", new() { ["language"] = "ru-RU", ["append_to_response"] = "credits,videos,watch/providers,keywords" }, ct);
            var x = json.RootElement;
            var movie = await db.Movies.FindAsync([id], ct) ?? new Movie { TmdbId = id };
            movie.Title = x.GetProperty("title").GetString() ?? movie.Title;
            movie.OriginalTitle = x.TryGetProperty("original_title", out var original) ? original.GetString() : null;
            movie.Overview = x.TryGetProperty("overview", out var overview) ? overview.GetString() : null;
            movie.ReleaseDate = x.TryGetProperty("release_date", out var date) ? date.GetString() : null;
            movie.RuntimeMinutes = x.TryGetProperty("runtime", out var runtime) && runtime.ValueKind != JsonValueKind.Null ? runtime.GetInt32() : null;
            movie.VoteAverage = x.GetProperty("vote_average").GetDouble();
            movie.VoteCount = x.GetProperty("vote_count").GetInt32();
            movie.Popularity = x.GetProperty("popularity").GetDouble();
            movie.PosterPath = x.TryGetProperty("poster_path", out var poster) ? poster.GetString() : null;
            movie.BackdropPath = x.TryGetProperty("backdrop_path", out var back) ? back.GetString() : null;
            movie.DetailsState = "ready";
            movie.Payload = x.GetRawText();
            if (db.Entry(movie).State == EntityState.Detached) db.Movies.Add(movie);
            await db.SaveChangesAsync(ct);
            return movie;
        }
        catch (HttpRequestException)
        {
            var fallback = FallbackCatalog.FirstOrDefault(x => x.TmdbId == id);
            if (fallback is null) throw;
            var existing = await db.Movies.FindAsync([id], ct);
            if (existing is not null) return existing;
            db.Movies.Add(fallback);
            await db.SaveChangesAsync(ct);
            return fallback;
        }
    }

    private async Task<List<Movie>> Fallback(string? search, int page, CancellationToken ct)
    {
        var query = FallbackCatalog.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Title.Contains(search, StringComparison.OrdinalIgnoreCase) || (x.OriginalTitle?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        var rows = query.Skip(Math.Max(0, page - 1) * 20).Take(20).ToList();
        foreach (var row in rows)
        {
            if (await db.Movies.FindAsync([row.TmdbId], ct) is null) db.Movies.Add(row);
        }
        await db.SaveChangesAsync(ct);
        return rows;
    }

    private static readonly Movie[] FallbackCatalog =
    [
        new() { TmdbId = 693134, Title = "Дюна: Часть вторая", OriginalTitle = "Dune: Part Two", Overview = "Пол Атрейдес объединяется с фрименами в войне против дома Харконненов.", ReleaseDate = "2024-02-27", RuntimeMinutes = 166, VoteAverage = 8.3, Popularity = 100, PosterPath = "/1pdfLvkbY9ohJlCjQH2CZjjYVvJ.jpg", BackdropPath = "/8b8R8l88Qje9dn9OE8PY05Nxl1X.jpg", DetailsState = "ready" },
        new() { TmdbId = 872585, Title = "Оппенгеймер", OriginalTitle = "Oppenheimer", Overview = "История физика Роберта Оппенгеймера и создания атомной бомбы.", ReleaseDate = "2023-07-19", RuntimeMinutes = 180, VoteAverage = 8.1, Popularity = 99, PosterPath = "/8Gxv8gSFCU0XGDykEGv7zR1n2ua.jpg", BackdropPath = "/fm6KqXpk3M2HVveHwCrBSSBaO0V.jpg", DetailsState = "ready" },
        new() { TmdbId = 157336, Title = "Интерстеллар", OriginalTitle = "Interstellar", Overview = "Команда исследователей отправляется через червоточину в поисках нового дома для человечества.", ReleaseDate = "2014-11-05", RuntimeMinutes = 169, VoteAverage = 8.4, Popularity = 98, PosterPath = "/gEU2QniE6E77NI6lCU6MxlNBvIx.jpg", BackdropPath = "/pbrkL804c8yAv3zBZR4QPEafpAR.jpg", DetailsState = "ready" },
        new() { TmdbId = 335984, Title = "Бегущий по лезвию 2049", OriginalTitle = "Blade Runner 2049", Overview = "Офицер К раскрывает тайну, способную погрузить общество в хаос.", ReleaseDate = "2017-10-04", RuntimeMinutes = 164, VoteAverage = 7.9, Popularity = 94, PosterPath = "/gajva2L0rPYkEWjzgFlBXCAVBE5.jpg", BackdropPath = "/ilrZsKbcurEznKKaUNRZTrM4BKM.jpg", DetailsState = "ready" },
        new() { TmdbId = 475557, Title = "Джокер", OriginalTitle = "Joker", Overview = "История превращения неудачливого комика Артура Флека в Джокера.", ReleaseDate = "2019-10-01", RuntimeMinutes = 122, VoteAverage = 8.0, Popularity = 93, PosterPath = "/udDclJoHjfjb8Ekgsd4FDteOkCU.jpg", BackdropPath = "/n6bUvigpRFqSwmPp1m2YADdbRBc.jpg", DetailsState = "ready" },
        new() { TmdbId = 496243, Title = "Паразиты", OriginalTitle = "Parasite", Overview = "Семья бедняков хитростью проникает в дом богатой семьи.", ReleaseDate = "2019-05-30", RuntimeMinutes = 132, VoteAverage = 8.2, Popularity = 92, PosterPath = "/7IiTTgloJzvGI1TAYymCfbfl3vT.jpg", BackdropPath = "/TU9NIjwzjoKPwQHoHshkFcQUCG.jpg", DetailsState = "ready" },
        new() { TmdbId = 27205, Title = "Начало", OriginalTitle = "Inception", Overview = "Вор, крадущий идеи через сны, получает невыполнимое задание.", ReleaseDate = "2010-07-15", RuntimeMinutes = 148, VoteAverage = 8.4, Popularity = 91, PosterPath = "/9gk7adHYeDvHkCSEqAvQNLV5Uge.jpg", BackdropPath = "/s3TBrRGB1iav7gFOCNx3H31MoES.jpg", DetailsState = "ready" },
        new() { TmdbId = 603, Title = "Матрица", OriginalTitle = "The Matrix", Overview = "Хакер Нео узнаёт, что привычный мир — иллюзия.", ReleaseDate = "1999-03-30", RuntimeMinutes = 136, VoteAverage = 8.2, Popularity = 90, PosterPath = "/f5uNbUC76oowt5mt5J9QlqrIYQ6.jpg", BackdropPath = "/icmmSD4vTTDKOq2vvdulafOGw93.jpg", DetailsState = "ready" },
        new() { TmdbId = 106646, Title = "Волк с Уолл-стрит", OriginalTitle = "The Wolf of Wall Street", Overview = "Возвышение и падение Джордана Белфорта — короля брокеров.", ReleaseDate = "2013-12-25", RuntimeMinutes = 180, VoteAverage = 8.0, Popularity = 88, PosterPath = "/34m2tygAYBGqA9MXKhRDtzYd4MR.jpg", BackdropPath = "/cUJF8n4C4oiup9Dgrwx0Y5EK2pD.jpg", DetailsState = "ready" },
        new() { TmdbId = 122, Title = "Властелин колец: Две крепости", OriginalTitle = "The Lord of the Rings: The Two Towers", Overview = "Братство разделилось, но путь к уничтожению кольца продолжается.", ReleaseDate = "2002-12-18", RuntimeMinutes = 179, VoteAverage = 8.4, Popularity = 87, PosterPath = "/5VTN0pR8gcqV3EPU7VgYdT4M6Y.jpg", BackdropPath = "/x2RS3uTcsJJ9IfjNPcgDmukoEcQ.jpg", DetailsState = "ready" }
    ];
}
