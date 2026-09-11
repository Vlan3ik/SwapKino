using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SwapKino.Api;
using Xunit;

namespace SwapKino.IntegrationTests;

public sealed class CoverageUnitTests
{
    [Fact]
    public async Task Tmdb_client_search_and_details_map_movie_and_series_payloads()
    {
        var body = """
            {"results":[{"id":7,"title":"Film","original_title":"Original","release_date":"2024-01-02","vote_average":8.4,"vote_count":12,"popularity":3.2,"poster_path":"/p.jpg","backdrop_path":"/b.jpg","overview":"Text","original_language":"ru","adult":false}],"total_pages":2,"total_results":3}
            """;
        var detailBody = """{"id":7,"title":"Film","original_title":"Original","release_date":"2024-01-02","vote_average":8.4,"vote_count":12,"popularity":3.2,"poster_path":"/p.jpg","backdrop_path":"/b.jpg","overview":"Text","original_language":"ru","adult":false}""";
        var factory = new StubHttpClientFactory(new SequenceHandler(
            (HttpStatusCode.OK, body), (HttpStatusCode.OK, body), (HttpStatusCode.OK, detailBody)));
        var client = new TmdbClient(factory, Config());

        var page = await client.SearchAsync("film", false, CancellationToken.None);
        var candidates = await client.SearchCandidatesAsync("film", true, CancellationToken.None);
        var summary = await client.UpsertSummary(JsonDocument.Parse("""{"id":8,"name":"Series","first_air_date":"2023-01-01"}""").RootElement, true, CancellationToken.None);
        var details = await client.Details(7, CancellationToken.None);

        Assert.Equal(3, page.TotalResults);
        Assert.Equal("Film", page.Results[0].Title);
        Assert.True(candidates[0].IsSeries);
        Assert.Equal("Series", summary.Title);
        Assert.Equal("Film", details.Title);
        Assert.Equal("/p.jpg", details.PosterPath);
    }

    [Fact]
    public async Task Tmdb_client_uses_bearer_token_and_retries_transient_failures()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.ServiceUnavailable, "{}"),
            (HttpStatusCode.OK, "{\"results\":[]}"));
        var client = new TmdbClient(new StubHttpClientFactory(handler), Config(("TMDB_ACCESS_TOKEN", "secret")));

        var result = await client.SearchAsync("retry", false, CancellationToken.None);

        Assert.Empty(result.Results);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
    }

    [Fact]
    public void Tmdb_card_maps_movie_and_series_fields_and_missing_images()
    {
        using var movieDoc = JsonDocument.Parse("""
            {"title":"Film","original_title":"Orig","overview":"Plot","release_date":"2020-01-01","runtime":121,"vote_average":7.5,"vote_count":42,"poster_path":"/poster.jpg","backdrop_path":"/backdrop.jpg","genres":[{"id":18,"name":"Drama"}]}
            """);
        using var seriesDoc = JsonDocument.Parse("""{"name":"Show","first_air_date":"2021-01-01","genres":[]}""");

        using var movieCard = JsonDocument.Parse(JsonSerializer.Serialize(TmdbCardGateway.Card(movieDoc.RootElement, new ProductDeckItem(1, false))));
        using var seriesCard = JsonDocument.Parse(JsonSerializer.Serialize(TmdbCardGateway.Card(seriesDoc.RootElement, new ProductDeckItem(2, true))));
        var movie = movieCard.RootElement;
        var series = seriesCard.RootElement;

        Assert.Equal("Film", movie.GetProperty("title").GetString());
        Assert.Equal(121, movie.GetProperty("runtime").GetInt32());
        Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", movie.GetProperty("posterUrl").GetString());
        Assert.Equal("Show", series.GetProperty("title").GetString());
        Assert.True(series.GetProperty("runtime").ValueKind == JsonValueKind.Null);
        Assert.Null(TmdbCardGateway.PosterUrl(seriesDoc.RootElement));
    }

    [Fact]
    public void Tmdb_card_uses_fallback_title_when_payload_is_incomplete()
    {
        using var document = JsonDocument.Parse("{} ");
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(TmdbCardGateway.Card(document.RootElement, new ProductDeckItem(99, false))));
        Assert.Equal("TMDB 99", serialized.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, serialized.RootElement.GetProperty("rating").GetDouble());
    }

    [Fact]
    public async Task Vibix_client_handles_configuration_missing_ids_and_successful_embed()
    {
        var notConfigured = new VibixClient(new HttpClient(new SequenceHandler()) { BaseAddress = new Uri("https://vibix.test/") }, Config());
        Assert.Equal("not_configured", (await notConfigured.FindAsync(null, null, CancellationToken.None)).Status);

        var configured = new VibixClient(new HttpClient(new SequenceHandler((HttpStatusCode.OK, """
            {"embed_code":"<div data-publisher-id='pub' data-type='movie' data-id='old'></div>","iframe_url":"https://player.test/video","name":"Film","quality":"HD"}
            """))) { BaseAddress = new Uri("https://vibix.test/") }, Config(("VIBIX_API_KEY", "token")));
        var result = await configured.FindAsync(42, "tt123", CancellationToken.None);

        Assert.Equal("available", result.Status);
        Assert.Equal("https://player.test/video", result.Video?.IframeUrl);
        Assert.Equal("imdb", result.Video?.Embed?.Type);
        Assert.Equal("tt123", result.Video?.Embed?.Id);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "not_published")]
    [InlineData(HttpStatusCode.BadRequest, "not_published")]
    [InlineData(HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "forbidden")]
    [InlineData(HttpStatusCode.InternalServerError, "upstream_error")]
    public async Task Vibix_client_maps_upstream_statuses(HttpStatusCode status, string expected)
    {
        var client = new VibixClient(new HttpClient(new SequenceHandler((status, "{}"))) { BaseAddress = new Uri("https://vibix.test/") }, Config(("VIBIX_API_KEY", "token")));
        var result = await client.FindAsync(null, "tt123", CancellationToken.None);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Vibix_client_falls_back_to_kinopoisk_and_rejects_invalid_payload()
    {
        var handler = new SequenceHandler(new(HttpStatusCode.NotFound, "{}"), new(HttpStatusCode.OK, "not-json"));
        var client = new VibixClient(new HttpClient(handler) { BaseAddress = new Uri("https://vibix.test/") }, Config(("VIBIX_API_KEY", "token")));
        var result = await client.FindAsync(77, "tt123", CancellationToken.None);
        Assert.Equal("upstream_error", result.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Recommendation_helpers_cover_empty_and_alias_paths()
    {
        Assert.Equal(0, RecommendationMetrics.NdcgAtK([], 3));
        Assert.Equal(0, RecommendationMetrics.RecallAtK([], [], 3));
        Assert.Equal(0, RecommendationMetrics.IntraListDiversity([new HashSet<int>()]));
        Assert.Equal(0, RecommendationMetrics.Coverage([1, 2], 0));
        Assert.Equal("comedy", ThemeRegistry.CanonicalSlug("posmeyatsya"));
        Assert.Equal("custom", ThemeRegistry.CanonicalSlug("custom"));
        Assert.NotNull(ThemeRegistry.Find("HORROR"));
        Assert.Null(ThemeRegistry.Find("missing"));
    }

    [Fact]
    public void Movie_dtos_preserve_summary_and_extract_detail_arrays_safely()
    {
        var movie = new Movie
        {
            TmdbId = 10, Title = "Film", OriginalTitle = "Original", IsSeries = false,
            VoteAverage = 8, VoteCount = 10, RuntimeMinutes = 100, PosterPath = "/p.jpg",
            BackdropPath = "/b.jpg", DetailsState = "ready",
            MovieGenres = [new MovieGenre { Genre = new Genre { TmdbId = 2, Slug = "z", Name = "Z" } }, new MovieGenre { Genre = new Genre { TmdbId = 1, Slug = "a", Name = "A" } }],
            Payload = """{"credits":{"cast":[{"id":1}],"crew":[{"id":2}]},"videos":{"results":[{"key":"v"}]},"images":{"backdrops":[{"file_path":"/b.jpg"}]},"watch/providers":{"results":{}}}"""
        };

        var summary = MovieDto.Summary(movie);
        var details = MovieDto.Details(movie);

        Assert.Equal(["A", "Z"], summary.Genres.Select(x => x.Name));
        Assert.Equal("https://image.tmdb.org/t/p/w500/p.jpg", summary.PosterUrl);
        Assert.Single(details.Cast);
        Assert.Single(details.Crew);
        Assert.Single(details.Trailers);
        Assert.Single(details.Images);
        Assert.NotNull(details.WatchProviders);
        movie.Payload = "invalid";
        Assert.Empty(MovieDto.Details(movie).Cast);
    }

    [Theory]
    [InlineData("impression", null, "read")]
    [InlineData("already_watched", null, "read")]
    [InlineData("swipe_right", null, "positive")]
    [InlineData("favorite", null, "strong_positive")]
    [InlineData("not_for_me", null, "strong_negative")]
    [InlineData("not_interested", null, "negative")]
    [InlineData("rating", 6.0, "read")]
    [InlineData("rating", 1.0, "negative")]
    [InlineData("unknown", null, null)]
    public void Feedback_normalizer_covers_all_action_families(string action, double? value, string? expected)
    {
        Assert.Equal(expected, RecommendationFeedback.Normalize(action, value)?.Type);
    }

    [Fact]
    public void Eligibility_query_and_theme_classifier_reject_unusable_items()
    {
        var eligible = new Movie { Title = "Good", DetailsState = "ready", VoteAverage = 7, VoteCount = 3, PosterPath = "/p" };
        var adult = new Movie { Title = "Adult", DetailsState = "ready", VoteAverage = 7, VoteCount = 3, Adult = true, PosterPath = "/p" };
        var items = new[] { eligible, adult }.AsQueryable();
        Assert.Single(RecommendationEligibility.Apply(items));
        Assert.True(RecommendationEligibility.IsEligible(eligible));
        Assert.False(RecommendationEligibility.IsEligible(new Movie { Title = "", DetailsState = "ready", VoteAverage = 7, VoteCount = 1 }));

        var excluded = new Movie { MovieGenres = [new MovieGenre { GenreId = 27 }, new MovieGenre { GenreId = 35 }], MovieKeywords = [new MovieKeyword { KeywordId = 12377 }] };
        Assert.Contains("horror", ThemeRegistry.Classify(excluded).Select(x => x.Slug));
        Assert.Contains("comedy", ThemeRegistry.Classify(excluded).Select(x => x.Slug));
        Assert.DoesNotContain("psychological", ThemeRegistry.Classify(excluded).Select(x => x.Slug));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();
}

file sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler handler;
    public StubHttpClientFactory(string body) : this(new SequenceHandler((HttpStatusCode.OK, body))) { }
    public StubHttpClientFactory(HttpMessageHandler handler) => this.handler = handler;
    public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("https://test.invalid/") };
}

file sealed class SequenceHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> responses;
    public List<HttpRequestMessage> Requests { get; } = [];
    public SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) => this.responses = new(responses);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = responses.Count > 0 ? responses.Dequeue() : (HttpStatusCode.OK, "{}");
        return Task.FromResult(new HttpResponseMessage(response.Item1) { Content = new StringContent(response.Item2, Encoding.UTF8, "application/json") });
    }
}
