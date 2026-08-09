using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace SwapKino.Api;

public sealed record TmdbPage(IReadOnlyList<Movie> Results, int TotalPages, int TotalResults);

public sealed class TmdbClient(IHttpClientFactory factory, IConfiguration config, SwapKinoDbContext db)
{
    public async Task<JsonDocument> Get(string path, Dictionary<string, string?> query, CancellationToken ct)
    {
        var token = config["TMDB_ACCESS_TOKEN"];
        var client = factory.CreateClient("tmdb");
        for (var attempt = 0; ; attempt++)
        {
            var values = new Dictionary<string,string?>(query);
            if (string.IsNullOrWhiteSpace(token)) values["api_key"] = config["TMDB_API_KEY"];
            var uri = path.TrimStart('/') + "?" + string.Join("&", values.Where(x => x.Value is not null).Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!)}"));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
            {
                // Parse directly from the response stream. ReadAsStringAsync would
                // retain a second, potentially very large, UTF-16 copy while the
                // JsonDocument builds its own representation.
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            }
            if (((int)response.StatusCode != 429 && (int)response.StatusCode < 500) || attempt >= 3) response.EnsureSuccessStatusCode();
            await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct);
        }
    }

    public async Task<TmdbPage> SearchAsync(string query, bool isSeries, CancellationToken ct)
    {
        using var json = await Get(isSeries ? "/search/tv" : "/search/movie", new()
        {
            ["language"] = "ru-RU",
            ["page"] = "1",
            ["query"] = query,
            ["include_adult"] = "false"
        }, ct);
        var results = json.RootElement.GetProperty("results").EnumerateArray()
            .Select(x => SummaryCandidate(x, isSeries)).ToList();
        return new(results,
            json.RootElement.TryGetProperty("total_pages", out var pages) ? pages.GetInt32() : 1,
            json.RootElement.TryGetProperty("total_results", out var total) ? total.GetInt32() : results.Count);
    }

    public async Task<TmdbPage> DiscoverPage(int page, string? search, CancellationToken ct, bool forceRefresh = false, string endpoint = "/discover/movie")
    {
        var isSeries = endpoint.Contains("/tv", StringComparison.Ordinal);
        // Only discover endpoints share the persisted catalog cache. Endpoint feeds
        // such as top_rated have their own ordering and must not be satisfied by a
        // popularity-sorted page from the global catalog.
        if (!forceRefresh && string.IsNullOrWhiteSpace(search) && endpoint.StartsWith("/discover/", StringComparison.Ordinal))
        {
            var cached = await db.Movies.AsNoTracking().Include(x => x.MovieGenres).ThenInclude(x => x.Genre)
                .Where(x => x.IsSeries == isSeries).OrderByDescending(x => x.Popularity).ThenBy(x => x.TmdbId)
                .Skip(Math.Max(0,page-1)*20).Take(20).ToListAsync(ct);
            if (cached.Count == 20)
            {
                var total = await db.Movies.CountAsync(x => x.IsSeries == isSeries, ct);
                return new(cached, Math.Max(1,(int)Math.Ceiling(total/20d)), total);
            }
        }
        return await FetchPage(page, search, isSeries, search is null ? endpoint : isSeries ? "/search/tv" : "/search/movie", ct);
    }

    public async Task<List<Movie>> Discover(int page, string? search, CancellationToken ct, bool forceRefresh = false, string endpoint = "/discover/movie")
        => (await DiscoverPage(page, search, ct, forceRefresh, endpoint)).Results.ToList();

    private async Task<TmdbPage> FetchPage(int page, string? search, bool isSeries, string endpoint, CancellationToken ct)
    {
        using var json = await Get(endpoint, new() { ["language"]="ru-RU", ["page"]=page.ToString(CultureInfo.InvariantCulture), ["query"]=search, ["include_adult"]="false", ["sort_by"]=endpoint.StartsWith("/discover/") ? "popularity.desc" : null }, ct);
        var result = new List<Movie>();
        foreach (var x in json.RootElement.GetProperty("results").EnumerateArray()) result.Add(await UpsertSummary(x, isSeries, ct));
        await db.SaveChangesAsync(ct);
        return new(result, json.RootElement.TryGetProperty("total_pages",out var pages)?pages.GetInt32():1, json.RootElement.TryGetProperty("total_results",out var total)?total.GetInt32():result.Count);
    }

    public async Task<Movie> UpsertSummary(JsonElement x, bool isSeries, CancellationToken ct)
    {
        var id = x.GetProperty("id").GetInt32();
        var movie = await db.Movies.Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre).SingleOrDefaultAsync(m => m.TmdbId == id && m.IsSeries == isSeries, ct) ?? new Movie { TmdbId=id, IsSeries=isSeries };
        movie.Title = String(x, isSeries ? "name" : "title") ?? movie.Title;
        movie.OriginalTitle = String(x, isSeries ? "original_name" : "original_title") ?? movie.OriginalTitle;
        movie.Overview = NonEmpty(String(x,"overview")) ?? movie.Overview;
        movie.OriginalLanguage = String(x,"original_language") ?? movie.OriginalLanguage;
        movie.ReleaseDate = NonEmpty(String(x,isSeries?"first_air_date":"release_date")) ?? movie.ReleaseDate;
        movie.VoteAverage = Number(x,"vote_average") ?? movie.VoteAverage;
        movie.VoteCount = Integer(x,"vote_count") ?? movie.VoteCount;
        movie.Popularity = Number(x,"popularity") ?? movie.Popularity;
        movie.PosterPath = String(x,"poster_path") ?? movie.PosterPath;
        movie.BackdropPath = String(x,"backdrop_path") ?? movie.BackdropPath;
        movie.SummaryUpdatedAt = movie.UpdatedAt = DateTime.UtcNow;
        // Never replace a detail payload with a discover/search summary.
        if (movie.DetailsState != "ready") movie.Payload = x.GetRawText();
        if (db.Entry(movie).State == EntityState.Detached) db.Movies.Add(movie);
        await SyncGenres(movie, x, isSeries, ct);
        return movie;
    }

    private static Movie SummaryCandidate(JsonElement x, bool isSeries) => new()
    {
        TmdbId = x.GetProperty("id").GetInt32(),
        IsSeries = isSeries,
        Title = String(x, isSeries ? "name" : "title") ?? "",
        OriginalTitle = String(x, isSeries ? "original_name" : "original_title"),
        Overview = NonEmpty(String(x, "overview")),
        OriginalLanguage = String(x, "original_language"),
        ReleaseDate = NonEmpty(String(x, isSeries ? "first_air_date" : "release_date")),
        VoteAverage = Number(x, "vote_average") ?? 0,
        VoteCount = Integer(x, "vote_count") ?? 0,
        Popularity = Number(x, "popularity") ?? 0,
        PosterPath = NonEmpty(String(x, "poster_path")),
        BackdropPath = NonEmpty(String(x, "backdrop_path")),
        // Search results are short-lived matching candidates, not persisted
        // details. Keeping every result's raw JSON multiplies import memory use.
        Payload = "{}"
    };

    public async Task<Movie> Details(int id, CancellationToken ct, bool isSeries = false)
    {
        var path = isSeries ? $"/tv/{id}" : $"/movie/{id}";
        JsonDocument json;
        try { json = await Get(path, new() { ["language"]="ru-RU", ["append_to_response"]="credits,videos,watch/providers,keywords,images,external_ids", ["include_image_language"]="ru,en,null" }, ct); }
        catch (HttpRequestException)
        {
            var stale = await db.Movies.Include(x=>x.MovieGenres).ThenInclude(x=>x.Genre).SingleOrDefaultAsync(x=>x.TmdbId==id&&x.IsSeries==isSeries,ct);
            if (stale is not null) { stale.DetailAttemptCount++; stale.DetailsState="failed"; await db.SaveChangesAsync(ct); return stale; }
            throw;
        }
        using var jsonLease = json;
        var x=json.RootElement;
        string? fallbackOverview=null,fallbackTagline=null;
        if(string.IsNullOrWhiteSpace(String(x,"overview"))||string.IsNullOrWhiteSpace(String(x,"tagline")))
        {
            try{using var en=await Get(path,new(){["language"]="en-US"},ct);fallbackOverview=NonEmpty(String(en.RootElement,"overview"));fallbackTagline=NonEmpty(String(en.RootElement,"tagline"));}catch(HttpRequestException){ }
        }
        var movie=await db.Movies.Include(m=>m.MovieGenres).ThenInclude(mg=>mg.Genre).SingleOrDefaultAsync(m=>m.TmdbId==id&&m.IsSeries==isSeries,ct) ?? new Movie{TmdbId=id,IsSeries=isSeries};
        movie.Title=String(x,isSeries?"name":"title")??movie.Title;
        movie.OriginalTitle=String(x,isSeries?"original_name":"original_title");
        movie.Tagline=NonEmpty(String(x,"tagline"))??fallbackTagline;
        movie.Overview=NonEmpty(String(x,"overview"))??fallbackOverview??movie.Overview;
        movie.OriginalLanguage=String(x,"original_language");
        movie.ReleaseDate=NonEmpty(String(x,isSeries?"first_air_date":"release_date"));
        movie.RuntimeMinutes=isSeries ? FirstRuntime(x) : Integer(x,"runtime");
        movie.VoteAverage=Number(x,"vote_average")??0; movie.VoteCount=Integer(x,"vote_count")??0; movie.Popularity=Number(x,"popularity")??0;
        // A localized details response can legitimately omit artwork. Keep known
        // paths instead of turning a previously displayable movie into a blank card.
        movie.PosterPath=NonEmpty(String(x,"poster_path"))??movie.PosterPath;
        movie.BackdropPath=NonEmpty(String(x,"backdrop_path"))??movie.BackdropPath;
        movie.DetailsState="ready"; movie.DetailAttemptCount=0; movie.Payload=x.GetRawText(); movie.DetailsUpdatedAt=movie.UpdatedAt=DateTime.UtcNow;
        if(db.Entry(movie).State==EntityState.Detached)db.Movies.Add(movie);
        await SyncGenres(movie,x,isSeries,ct); await db.SaveChangesAsync(ct); return movie;
    }

    private async Task SyncGenres(Movie movie, JsonElement x, bool isSeries, CancellationToken ct)
    {
        var ids = new List<(int id,string? name)>();
        if(x.TryGetProperty("genre_ids",out var rawIds)&&rawIds.ValueKind==JsonValueKind.Array) ids.AddRange(rawIds.EnumerateArray().Select(v=>(v.GetInt32(),(string?)null)));
        if(x.TryGetProperty("genres",out var rawGenres)&&rawGenres.ValueKind==JsonValueKind.Array) ids.AddRange(rawGenres.EnumerateArray().Where(v=>v.TryGetProperty("id",out _)).Select(v=>(v.GetProperty("id").GetInt32(),String(v,"name"))));
        foreach(var (genreId,name) in ids.DistinctBy(v=>v.id))
        {
            var genre=await db.Genres.FindAsync([genreId],ct);
            if(genre is null){genre=new Genre{TmdbId=genreId,Name=name??GenreNames.GetValueOrDefault(genreId)??genreId.ToString(),Slug=Slug(name??GenreNames.GetValueOrDefault(genreId)??genreId.ToString()),IsSeries=isSeries};db.Genres.Add(genre);}
            else if(!string.IsNullOrWhiteSpace(name)){genre.Name=name;genre.Slug=Slug(name);}
            if(movie.MovieGenres.All(g=>g.GenreId!=genreId)) movie.MovieGenres.Add(new MovieGenre{TmdbId=movie.TmdbId,IsSeries=movie.IsSeries,GenreId=genreId,Movie=movie,Genre=genre});
        }
    }
    private static string? String(JsonElement x,string key)=>x.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    private static string? NonEmpty(string? value)=>string.IsNullOrWhiteSpace(value)?null:value;
    private static double? Number(JsonElement x,string key)=>x.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out var n)?n:null;
    private static int? Integer(JsonElement x,string key)=>x.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out var n)?n:null;
    private static int? FirstRuntime(JsonElement x)=>x.TryGetProperty("episode_run_time",out var r)&&r.ValueKind==JsonValueKind.Array?r.EnumerateArray().Select(v=>v.TryGetInt32(out var n)?(int?)n:null).FirstOrDefault(n=>n>0):null;
    private static string Slug(string value)=>Regex.Replace(value.ToLowerInvariant(),"[^a-z0-9а-яё]+","-").Trim('-');
    private static readonly Dictionary<int,string> GenreNames=new(){{28,"Боевик"},{12,"Приключения"},{16,"Анимация"},{35,"Комедия"},{80,"Криминал"},{99,"Документальный"},{18,"Драма"},{10751,"Семейный"},{14,"Фэнтези"},{36,"История"},{27,"Ужасы"},{10402,"Музыка"},{9648,"Детектив"},{10749,"Мелодрама"},{878,"Фантастика"},{10770,"Телевизионный фильм"},{53,"Триллер"},{10752,"Военный"},{37,"Вестерн"},{10759,"Боевик и приключения"},{10762,"Детский"},{10763,"Новости"},{10764,"Реалити"},{10765,"Фантастика и фэнтези"},{10766,"Мыльная опера"},{10767,"Ток-шоу"},{10768,"Война и политика"}};
}
