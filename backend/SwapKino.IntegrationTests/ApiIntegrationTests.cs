extern alias worker;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SwapKino.Api;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace SwapKino.IntegrationTests;

[Collection("api-integration")]
public sealed class ApiIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    private readonly RedisContainer redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
    private SwapKinoApiFactory factory = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await redis.StartAsync();
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("DATABASE_URL", postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("REDIS_URL", redis.GetConnectionString());
        Environment.SetEnvironmentVariable("REDIS_CACHE_URL", redis.GetConnectionString());
        Environment.SetEnvironmentVariable("JWT_SECRET", "integration-test-secret-0123456789-abcdef");
        Environment.SetEnvironmentVariable("TMDB_ALLOW_FALLBACK", "false");
        factory = new SwapKinoApiFactory();
        client = factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        factory.Dispose();
        await redis.DisposeAsync();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_create_full_identity_schema_and_health_is_live()
    {
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('AspNetUserRoles','AspNetUserClaims','AspNetUserLogins','AspNetUserTokens','AspNetRoleClaims')", connection);
        Assert.Equal(5L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Registration_and_duplicate_email_are_handled()
    {
        var request = new { email = "integration@example.test", password = "IntegrationPass123!", displayName = "Integration" };
        var first = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        var second = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Readiness_requires_worker_heartbeat()
    {
        var response = await client.GetAsync("/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_filters_globally_and_cursor_has_no_duplicates()
    {
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
            var thriller=new Genre{TmdbId=53,Slug="thriller",Name="Триллер"};var comedy=new Genre{TmdbId=35,Slug="comedy",Name="Комедия"};db.Genres.AddRange(thriller,comedy);
            var first=new Movie{TmdbId=101,Title="First",VoteAverage=9,VoteCount=100,Popularity=20,ReleaseDate="2024-01-01"};var second=new Movie{TmdbId=102,Title="Second",VoteAverage=8,VoteCount=90,Popularity=10,ReleaseDate="2023-01-01"};var series=new Movie{TmdbId=101,IsSeries=true,Title="Series",VoteAverage=10,VoteCount=200,Popularity=30,ReleaseDate="2024-01-01"};db.Movies.AddRange(first,second,series);
            db.MovieGenres.AddRange(new MovieGenre{TmdbId=101,GenreId=53,Movie=first,Genre=thriller},new MovieGenre{TmdbId=102,GenreId=35,Movie=second,Genre=comedy},new MovieGenre{TmdbId=101,IsSeries=true,GenreId=53,Movie=series,Genre=thriller});await db.SaveChangesAsync();
        }
        var page1=await client.GetFromJsonAsync<JsonElement>("/api/v1/movies?genreIds=53&sort=rating&limit=1");
        Assert.Equal(2,page1.GetProperty("totalCount").GetInt32());Assert.Equal(101,page1.GetProperty("items")[0].GetProperty("tmdbId").GetInt32());Assert.True(page1.GetProperty("items")[0].GetProperty("isSeries").GetBoolean());
        var cursor=Uri.EscapeDataString(page1.GetProperty("nextCursor").GetString()!);var page2=await client.GetFromJsonAsync<JsonElement>($"/api/v1/movies?genreIds=53&sort=rating&limit=1&cursor={cursor}");
        Assert.False(page2.GetProperty("items")[0].GetProperty("isSeries").GetBoolean());
    }

    [Fact]
    public async Task Reels_expose_genres_and_distinct_covers_from_their_own_candidates()
    {
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
            var thriller=await db.Genres.SingleOrDefaultAsync(x=>x.TmdbId==53);
            if(thriller is null){thriller=new Genre{TmdbId=53,Slug="thriller",Name="Триллер"};db.Genres.Add(thriller);}
            for(var index=0;index<4;index++)
            {
                var id=5100+index;if(await db.Movies.AnyAsync(x=>x.TmdbId==id&&!x.IsSeries))continue;
                var movie=new Movie{TmdbId=id,Title=$"Reel candidate {index}",BackdropPath=$"/reel-{index}.jpg",Popularity=100-index,VoteAverage=8,VoteCount=1000-index};
                db.Movies.Add(movie);db.MovieGenres.Add(new MovieGenre{TmdbId=id,GenreId=53,Movie=movie,Genre=thriller});
            }
            await db.SaveChangesAsync();
        }

        var response=await client.GetFromJsonAsync<JsonElement>("/api/v1/reels");
        var first=response.GetProperty("items").EnumerateArray().Take(3).ToArray();
        Assert.All(first,reel=>
        {
            Assert.NotEmpty(reel.GetProperty("genres").EnumerateArray());
            Assert.False(string.IsNullOrWhiteSpace(reel.GetProperty("coverUrl").GetString()));
            Assert.Equal(reel.GetProperty("coverUrl").GetString(),reel.GetProperty("representativeMovie").GetProperty("backdropUrl").GetString());
        });
        Assert.True(first.Select(x=>x.GetProperty("coverUrl").GetString()).Distinct().Count()>1);

        var feed=await client.GetFromJsonAsync<JsonElement>("/api/v1/reels/na-odnom-dyhanii/feed?limit=2");
        var metadata=feed.GetProperty("reel");
        Assert.NotEmpty(metadata.GetProperty("genres").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(metadata.GetProperty("coverUrl").GetString()));
        Assert.Equal(metadata.GetProperty("coverUrl").GetString(),metadata.GetProperty("representativeMovie").GetProperty("backdropUrl").GetString());
    }

    [Fact]
    public async Task Reels_and_catalog_do_not_materialize_large_detail_payloads()
    {
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
            var genre=await db.Genres.SingleOrDefaultAsync(x=>x.TmdbId==878);
            if(genre is null){genre=new Genre{TmdbId=878,Slug="science-fiction",Name="Фантастика"};db.Genres.Add(genre);}
            var largePayload="{\"credits\":\""+new string('x',2*1024*1024)+"\"}";
            for(var index=0;index<12;index++)
            {
                var id=6100+index;
                if(await db.Movies.AnyAsync(x=>x.TmdbId==id&&!x.IsSeries))continue;
                var movie=new Movie{TmdbId=id,Title=$"Large payload {index}",BackdropPath=$"/large-{index}.jpg",Popularity=200-index,VoteAverage=8,VoteCount=500,DetailsState="ready",Payload=largePayload};
                db.Movies.Add(movie);db.MovieGenres.Add(new MovieGenre{TmdbId=id,GenreId=878,Movie=movie,Genre=genre});
            }
            await db.SaveChangesAsync();
        }

        using var reels=await client.GetAsync("/api/v1/reels");
        Assert.Equal(HttpStatusCode.OK,reels.StatusCode);
        var reelBody=await reels.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(reelBody.GetProperty("items").EnumerateArray());

        using var catalog=await client.GetAsync("/api/v1/movies?genreIds=878&limit=10");
        Assert.Equal(HttpStatusCode.OK,catalog.StatusCode);
        var catalogBody=await catalog.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10,catalogBody.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Import_matching_projection_does_not_read_large_payloads()
    {
        await using var scope=factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        db.Movies.Add(new Movie
        {
            TmdbId=6201,Title="Lightweight match",OriginalTitle="Lightweight match",
            ReleaseDate="2024-01-01",VoteCount=123,
            DetailsState="ready",Payload="{\"credits\":\""+new string('x',2*1024*1024)+"\"}"
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var query=worker::ImportQueries.LightweightMovies(db.Movies.AsNoTracking().Where(x=>x.TmdbId==6201));
        Assert.DoesNotContain("\"Payload\"",query.ToQueryString(),StringComparison.Ordinal);
        var candidate=await query.SingleAsync();
        Assert.Equal("Lightweight match",candidate.Title);
        Assert.Equal("{}",candidate.Payload);
        Assert.Empty(db.ChangeTracker.Entries<Movie>());
    }

    [Fact]
    public async Task Library_keeps_rating_favorite_and_watched_as_independent_state()
    {
        var auth=await (await client.PostAsJsonAsync("/api/v1/auth/register",new{email="state@example.test",password="IntegrationPass123!",displayName="State"})).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",auth.GetProperty("accessToken").GetString());
        await using(var scope=factory.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();db.Movies.Add(new Movie{TmdbId=777,Title="State movie"});await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/v1/actions",new{tmdbId=777,actionType="rating",value=8,idempotencyKey="rate-777"})).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/v1/actions",new{tmdbId=777,actionType="favorite",value=(double?)null,idempotencyKey="favorite-777"})).StatusCode);
        var library=await client.GetFromJsonAsync<JsonElement>("/api/v1/library");var item=library.GetProperty("items")[0];Assert.Equal(8,item.GetProperty("rating").GetDouble());Assert.True(item.GetProperty("favorite").GetBoolean());Assert.True(item.GetProperty("watched").GetBoolean());
    }

    [Fact]
    public async Task Profile_favorites_and_ratings_are_paginated_and_include_statistics()
    {
        var auth=await (await client.PostAsJsonAsync("/api/v1/auth/register",new{email="profile@example.test",password="IntegrationPass123!",displayName="Profile"})).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",auth.GetProperty("accessToken").GetString());
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
            db.Movies.AddRange(new Movie{TmdbId=778,Title="Profile favorite",VoteAverage=8,VoteCount=100,ReleaseDate="2024-01-01"},new Movie{TmdbId=779,Title="Profile rating",VoteAverage=7,VoteCount=90,ReleaseDate="2023-01-01"});
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/v1/actions",new{tmdbId=778,actionType="favorite",idempotencyKey="profile-favorite"})).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await client.PostAsJsonAsync("/api/v1/actions",new{tmdbId=779,actionType="rating",value=9,idempotencyKey="profile-rating"})).StatusCode);
        var profile=await client.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        Assert.Equal(1,profile.GetProperty("statistics").GetProperty("favoritesCount").GetInt32());
        Assert.Equal(1,profile.GetProperty("statistics").GetProperty("ratingsCount").GetInt32());
        Assert.Equal(1,profile.GetProperty("previews").GetProperty("favorites").GetArrayLength());
        var ratings=await client.GetFromJsonAsync<JsonElement>("/api/v1/ratings?limit=1&minRating=6&sort=rating");
        Assert.Equal(1,ratings.GetProperty("totalCount").GetInt32());
        Assert.Equal(9,ratings.GetProperty("items")[0].GetProperty("rating").GetDouble());
        Assert.Equal("Profile rating",ratings.GetProperty("items")[0].GetProperty("movie").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Summary_upsert_never_erases_detail_payload_or_runtime()
    {
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        const string details="{\"id\":900,\"title\":\"Detailed\",\"runtime\":155,\"credits\":{\"cast\":[{\"id\":1}]},\"genres\":[{\"id\":53,\"name\":\"Триллер\"}]}";
        db.Movies.Add(new Movie{TmdbId=900,Title="Detailed",RuntimeMinutes=155,DetailsState="ready",Payload=details,DetailsUpdatedAt=DateTime.UtcNow});await db.SaveChangesAsync();
        var tmdb=scope.ServiceProvider.GetRequiredService<TmdbClient>();using var summary=JsonDocument.Parse("{\"id\":900,\"title\":\"Summary title\",\"overview\":\"short\",\"genre_ids\":[53]}");
        await tmdb.UpsertSummary(summary.RootElement,false,CancellationToken.None);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var movie=await db.Movies.SingleAsync(x=>x.TmdbId==900&&!x.IsSeries);Assert.Equal(155,movie.RuntimeMinutes);Assert.Equal("ready",movie.DetailsState);Assert.Equal(details,movie.Payload);
    }

    [Fact]
    public async Task Tmdb_search_returns_detached_candidates_without_polluting_catalog()
    {
        await using var scope=factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        var body="""{"page":1,"total_pages":1,"total_results":2,"results":[{"id":8101,"title":"Wanted","release_date":"2024-01-01","poster_path":"/wanted.jpg","irrelevant_raw_data":"This must not be retained by the candidate"},{"id":8102,"title":"Unrelated","release_date":"1999-01-01","poster_path":null}]}""";
        var tmdb=new TmdbClient(new StubHttpClientFactory(body),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TMDB_API_KEY","test"}}).Build(),db);

        var page=await tmdb.SearchAsync("Wanted",false,CancellationToken.None);

        Assert.Equal(2,page.Results.Count);
        Assert.Equal("/wanted.jpg",page.Results[0].PosterPath);
        Assert.All(page.Results,candidate=>Assert.Equal("{}",candidate.Payload));
        Assert.False(await db.Movies.AnyAsync(x=>x.TmdbId==8101||x.TmdbId==8102));
        Assert.Empty(db.ChangeTracker.Entries<Movie>());
    }

    [Fact]
    public async Task Tmdb_details_with_null_artwork_keeps_existing_paths()
    {
        await using var scope=factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        db.Movies.Add(new Movie{TmdbId=8201,Title="Existing",PosterPath="/poster.jpg",BackdropPath="/backdrop.jpg"});
        await db.SaveChangesAsync();
        var body="""{"id":8201,"title":"Existing","original_title":"Existing","overview":"Overview","tagline":"Tagline","release_date":"2024-01-01","runtime":100,"vote_average":7.5,"vote_count":100,"popularity":5,"poster_path":null,"backdrop_path":null,"genres":[]}""";
        var tmdb=new TmdbClient(new StubHttpClientFactory(body),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TMDB_API_KEY","test"}}).Build(),db);

        await tmdb.Details(8201,CancellationToken.None);

        db.ChangeTracker.Clear();
        var movie=await db.Movies.SingleAsync(x=>x.TmdbId==8201&&!x.IsSeries);
        Assert.Equal("/poster.jpg",movie.PosterPath);
        Assert.Equal("/backdrop.jpg",movie.BackdropPath);
    }

    [Fact]
    public async Task Tmdb_tv_details_persist_selected_payload_and_series_identity()
    {
        await using var scope=factory.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        var body="""{"id":8301,"name":"Selected series","original_name":"Selected series","overview":"Overview","tagline":"Tagline","first_air_date":"2025-02-03","episode_run_time":[52],"vote_average":8.1,"vote_count":250,"popularity":12,"poster_path":"/series.jpg","backdrop_path":"/series-bg.jpg","genres":[],"credits":{"cast":[{"id":7}]}}""";
        var tmdb=new TmdbClient(new StubHttpClientFactory(body),new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"TMDB_API_KEY","test"}}).Build(),db);

        await tmdb.Details(8301,CancellationToken.None,isSeries:true);

        db.ChangeTracker.Clear();
        var series=await db.Movies.SingleAsync(x=>x.TmdbId==8301&&x.IsSeries);
        Assert.Equal("Selected series",series.Title);
        Assert.Equal(52,series.RuntimeMinutes);
        Assert.Equal("ready",series.DetailsState);
        Assert.Contains("\"credits\"",series.Payload,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_import_with_staged_items_can_resume_without_losing_checkpoint()
    {
        var auth=await (await client.PostAsJsonAsync("/api/v1/auth/register",new{email="resume-import@example.test",password="IntegrationPass123!",displayName="Resume"})).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",auth.GetProperty("accessToken").GetString());
        var userId=auth.GetProperty("user").GetProperty("id").GetGuid();
        var job=new ImportJob{UserId=userId,ProfileUrl="https://www.kinopoisk.ru/user/12345/",Status="Failed",Phase="Matching",Progress=40,PhaseProgress=0,DiscoveredCount=12,PagesProcessed=2,PagesTotal=2,Checkpoint="{\"phase\":\"Matching\",\"progress\":40}",Error="System.OutOfMemoryException"};
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
            db.ImportJobs.Add(job);
            db.ImportItems.Add(new ImportItem{ImportJobId=job.Id,ExternalId="123",Title="Staged movie",KinopoiskUrl="https://www.kinopoisk.ru/film/123/",Page=1});
            await db.SaveChangesAsync();
        }

        using var response=await client.PostAsync($"/api/v1/imports/{job.Id}/resume",null);

        Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        var body=await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Queued",body.GetProperty("status").GetString());
        Assert.Equal(40,body.GetProperty("progress").GetInt32());
        await using var verificationScope=factory.Services.CreateAsyncScope();
        var verificationDb=verificationScope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        var saved=await verificationDb.ImportJobs.AsNoTracking().SingleAsync(x=>x.Id==job.Id);
        Assert.Equal("Queued",saved.Status);
        Assert.Equal("Matching",saved.Phase);
        Assert.Equal(40,saved.Progress);
        Assert.Equal(12,saved.DiscoveredCount);
        Assert.Equal("{\"phase\":\"Matching\",\"progress\":40}",saved.Checkpoint);
        Assert.Null(saved.Error);
        Assert.Single(await verificationDb.ImportItems.AsNoTracking().Where(x=>x.ImportJobId==job.Id).ToListAsync());
        var importEvent=await verificationDb.OutboxEvents.AsNoTracking().Where(x=>x.Topic=="kinopoisk.import").OrderByDescending(x=>x.CreatedAt).FirstAsync();
        var eventPayload=JsonSerializer.Deserialize<JsonElement>(importEvent.Payload);
        Assert.Equal(job.Id,eventPayload.GetProperty("jobId").GetGuid());
    }
}

file sealed class StubHttpClientFactory(string body) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new StubHttpHandler(body)){BaseAddress=new Uri("https://tmdb.test/")};
}

file sealed class StubHttpHandler(string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json")});
}

public sealed class SwapKinoApiFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> { }

[CollectionDefinition("api-integration", DisableParallelization = true)]
public sealed class ApiIntegrationCollection : ICollectionFixture<SwapKinoApiFactory> { }
