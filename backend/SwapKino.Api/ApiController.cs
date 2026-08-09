using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Security.Cryptography;

namespace SwapKino.Api;
[ApiController]
[Route("api/v1")]
public sealed class ApiController(SwapKinoDbContext db, UserManager<User> users, SignInManager<User> signIn, IConfiguration config, IDistributedCache cache, IHttpClientFactory http, TmdbClient tmdb) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Token(User user) { var key=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SECRET"]!)); var creds=new SigningCredentials(key,SecurityAlgorithms.HmacSha256); return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims:[new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Email,user.Email!)],expires:DateTime.UtcNow.AddHours(2),signingCredentials:creds)); }
    private async Task<string> CreateRefreshSession(User user, CancellationToken ct)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        db.RefreshSessions.Add(new RefreshSession { UserId = user.Id, TokenHash = hash, ExpiresAt = DateTime.UtcNow.AddDays(30) });
        await db.SaveChangesAsync(ct);
        Response.Cookies.Append("swapkino-refresh", raw, new CookieOptions { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(30), Path = "/api/v1/auth" });
        return raw;
    }
    private async Task<IActionResult> AuthResponse(User user, CancellationToken ct, int statusCode = StatusCodes.Status200OK)
    {
        await CreateRefreshSession(user, ct);
        return StatusCode(statusCode, new { accessToken = Token(user), user = new { id = user.Id, email = user.Email, displayName = user.DisplayName } });
    }
    [HttpPost("auth/register")][EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) { if(await users.FindByEmailAsync(request.Email) is not null)return Conflict(new{message="Email already registered"}); var u=new User{UserName=request.Email,Email=request.Email,DisplayName=request.DisplayName}; var result=await users.CreateAsync(u,request.Password); if(!result.Succeeded)return BadRequest(new { message = string.Join(" ", result.Errors.Select(error => error.Description)), errors = result.Errors }); return await AuthResponse(u, ct, StatusCodes.Status201Created); }
    [HttpPost("auth/login")][EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var u = await users.FindByEmailAsync(request.Email) ?? await users.FindByNameAsync(request.Email);
        if (u is null) return Unauthorized(new { message = "Invalid email or password" });
        var result = await signIn.CheckPasswordSignInAsync(u, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut) return StatusCode(StatusCodes.Status423Locked, new { message = "Слишком много неудачных попыток. Попробуйте через 15 минут." });
        if (!result.Succeeded) return Unauthorized(new { message = "Invalid email or password" });
        return await AuthResponse(u, ct);
    }
    [HttpPost("auth/refresh")][EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue("swapkino-refresh", out var raw) || string.IsNullOrWhiteSpace(raw)) return Unauthorized();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        var session = await db.RefreshSessions.FirstOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow, ct);
        if (session is null) return Unauthorized();
        var user = await users.FindByIdAsync(session.UserId.ToString());
        if (user is null) return Unauthorized();
        session.RevokedAt = DateTime.UtcNow;
        return await AuthResponse(user, ct);
    }
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue("swapkino-refresh", out var raw))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            var session = await db.RefreshSessions.FirstOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null, ct);
            if (session is not null) { session.RevokedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
        }
        Response.Cookies.Delete("swapkino-refresh", new CookieOptions { Path = "/api/v1/auth" });
        return NoContent();
    }
    [HttpGet("auth/me")][Authorize]
    public async Task<IActionResult> Me() { var user=await users.FindByIdAsync(UserId.ToString()); return user is null?Unauthorized():Ok(new{id=user.Id,email=user.Email,displayName=user.DisplayName,createdAt=user.CreatedAt}); }
    [HttpGet("profile")][Authorize]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null) return Unauthorized();
        var states = db.UserMovieStates.AsNoTracking().Where(x => x.UserId == UserId);
        var favoritesCount = await states.CountAsync(x => x.Favorite, ct);
        var ratingsCount = await states.CountAsync(x => x.Rating != null, ct);
        var watchedCount = await states.CountAsync(x => x.Watched, ct);
        var libraryCount = await states.CountAsync(ct);
        var averageRating = await states.Where(x => x.Rating != null).Select(x => x.Rating).AverageAsync(ct) ?? 0;
        var favoritePreview = await LibraryItems(states.Where(x => x.Favorite), "recent", 5, ct);
        var ratingPreview = await LibraryItems(states.Where(x => x.Rating != null), "rating", 5, ct);
        return Ok(new
        {
            user = new { id = user.Id, email = user.Email, displayName = user.DisplayName, createdAt = user.CreatedAt },
            statistics = new { favoritesCount, ratingsCount, watchedCount, libraryCount, averageRating = Math.Round(averageRating, 2) },
            previews = new { favorites = favoritePreview, ratings = ratingPreview }
        });
    }

    [HttpGet("favorites")][Authorize]
    public Task<IActionResult> Favorites([FromQuery] LibraryQuery query, CancellationToken ct) => UserLibrary(query, favorite: true, ct);

    [HttpGet("ratings")][Authorize]
    public Task<IActionResult> Ratings([FromQuery] LibraryQuery query, CancellationToken ct) => UserLibrary(query, favorite: false, ct);

    private async Task<IActionResult> UserLibrary(LibraryQuery request, bool favorite, CancellationToken ct)
    {
        if (request.Limit is < 1 or > 50) return ValidationProblem("Limit должен быть от 1 до 50");
        if (request.Page is < 1 or > 500) return ValidationProblem("Страница должна быть от 1 до 500");
        var sort = string.IsNullOrWhiteSpace(request.Sort) ? "recent" : request.Sort.ToLowerInvariant();
        if (sort is not ("recent" or "oldest" or "rating" or "title" or "newest")) return ValidationProblem("Неподдерживаемая сортировка");
        var after = ParseLibraryCursor(request.Cursor);
        if (request.Cursor is not null && after is null) return ValidationProblem("Некорректный cursor");
        var genreIds = ParseGenreIds(request.GenreIds);
        var states = db.UserMovieStates.AsNoTracking().Where(x => x.UserId == UserId && (favorite ? x.Favorite : x.Rating != null));
        states = states.Where(x => db.Movies.Any(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries));
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim().ToLower();
            states = states.Where(x => db.Movies.Any(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries && (m.Title.ToLower().Contains(term) || (m.OriginalTitle != null && m.OriginalTitle.ToLower().Contains(term)))));
        }
        if (genreIds.Length > 0) states = states.Where(x => db.MovieGenres.Any(g => g.TmdbId == x.TmdbId && g.IsSeries == x.IsSeries && genreIds.Contains(g.GenreId)));
        if (request.MinRating is not null) states = states.Where(x => db.Movies.Any(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries && m.VoteAverage >= request.MinRating && m.VoteCount > 0));
        if (request.YearFrom is not null) states = states.Where(x => db.Movies.Any(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries && m.ReleaseDate != null && string.Compare(m.ReleaseDate, request.YearFrom + "-01-01") >= 0));
        if (request.YearTo is not null) states = states.Where(x => db.Movies.Any(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries && m.ReleaseDate != null && string.Compare(m.ReleaseDate, request.YearTo + "-12-31") <= 0));
        if (request.IsSeries is not null) states = states.Where(x => x.IsSeries == request.IsSeries);

        var totalCount = await states.CountAsync(ct);
        if (after is not null) states = ApplyLibraryCursor(states, after, sort);
        states = sort switch
        {
            "rating" => states.OrderByDescending(x => x.Rating).ThenByDescending(x => x.UpdatedAt).ThenBy(x => x.TmdbId).ThenBy(x => x.IsSeries),
            "oldest" => states.OrderBy(x => x.UpdatedAt).ThenBy(x => x.TmdbId).ThenBy(x => x.IsSeries),
            "title" => states.OrderBy(x => db.Movies.Where(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries).Select(m => m.Title).FirstOrDefault()).ThenBy(x => x.TmdbId).ThenBy(x => x.IsSeries),
            "newest" => states.OrderByDescending(x => db.Movies.Where(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries).Select(m => m.ReleaseDate).FirstOrDefault()).ThenBy(x => x.TmdbId).ThenBy(x => x.IsSeries),
            _ => states.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.TmdbId).ThenBy(x => x.IsSeries)
        };
        if (after is null && request.Page > 1) states = states.Skip((request.Page - 1) * request.Limit);
        var rows = await states.Take(request.Limit + 1).ToListAsync(ct);
        var hasNext = rows.Count > request.Limit;
        if (hasNext) rows.RemoveAt(rows.Count - 1);
        var movies = await LightweightMovies(db.Movies.AsNoTracking().Where(x => rows.Select(s => s.TmdbId).Contains(x.TmdbId))).ToListAsync(ct);
        var items = rows.Select(state => LibraryItem(state, movies.FirstOrDefault(movie => movie.TmdbId == state.TmdbId && movie.IsSeries == state.IsSeries))).Where(x => x is not null).ToArray();
        var nextCursor = hasNext && rows.Count > 0 ? MakeLibraryCursor(rows[^1], sort, movies) : null;
        return Ok(new { items, page = request.Page, pageSize = request.Limit, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)request.Limit), hasNextPage = hasNext, nextCursor });
    }

    private async Task<object[]> LibraryItems(IQueryable<UserMovieState> states, string sort, int limit, CancellationToken ct)
    {
        var rows = await states.OrderByDescending(x => x.UpdatedAt).Take(limit).ToListAsync(ct);
        var movies = await LightweightMovies(db.Movies.AsNoTracking().Where(x => rows.Select(s => s.TmdbId).Contains(x.TmdbId))).ToListAsync(ct);
        return rows.Select(state => LibraryItem(state, movies.FirstOrDefault(movie => movie.TmdbId == state.TmdbId && movie.IsSeries == state.IsSeries))).Where(x => x is not null).Cast<object>().ToArray();
    }

    private static object? LibraryItem(UserMovieState state, Movie? movie) => movie is null ? null : new { state.TmdbId, state.IsSeries, state.Rating, state.Favorite, state.Watched, state.UpdatedAt, movie = MovieDto.Summary(movie) };

    private static int[] ParseGenreIds(string? value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => int.TryParse(x, out var id) ? id : 0).Where(x => x > 0).Distinct().ToArray();
    private static LibraryCursor? ParseLibraryCursor(string? cursor) { if (string.IsNullOrWhiteSpace(cursor)) return null; try { return JsonSerializer.Deserialize<LibraryCursor>(Encoding.UTF8.GetString(Convert.FromBase64String(cursor))); } catch { return null; } }
    private IQueryable<UserMovieState> ApplyLibraryCursor(IQueryable<UserMovieState> query, LibraryCursor cursor, string sort) => sort switch
    {
        "rating" => query.Where(x => x.Rating < cursor.UserRating || x.Rating == cursor.UserRating && (x.UpdatedAt < cursor.UpdatedAt || x.UpdatedAt == cursor.UpdatedAt && (x.TmdbId > cursor.TmdbId || x.TmdbId == cursor.TmdbId && x.IsSeries && !cursor.IsSeries))),
        "oldest" => query.Where(x => x.UpdatedAt > cursor.UpdatedAt || x.UpdatedAt == cursor.UpdatedAt && (x.TmdbId > cursor.TmdbId || x.TmdbId == cursor.TmdbId && x.IsSeries && !cursor.IsSeries)),
        "title" => query.Where(x => string.Compare(db.Movies.Where(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries).Select(m => m.Title).FirstOrDefault(), cursor.Text) > 0 || db.Movies.Where(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries).Select(m => m.Title).FirstOrDefault() == cursor.Text && (x.TmdbId > cursor.TmdbId || x.TmdbId == cursor.TmdbId && x.IsSeries && !cursor.IsSeries)),
        "newest" => query.Where(x => string.Compare(db.Movies.Where(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries).Select(m => m.ReleaseDate).FirstOrDefault(), cursor.Text) < 0 || db.Movies.Where(m => m.TmdbId == x.TmdbId && m.IsSeries == x.IsSeries).Select(m => m.ReleaseDate).FirstOrDefault() == cursor.Text && (x.TmdbId > cursor.TmdbId || x.TmdbId == cursor.TmdbId && x.IsSeries && !cursor.IsSeries)),
        _ => query.Where(x => x.UpdatedAt < cursor.UpdatedAt || x.UpdatedAt == cursor.UpdatedAt && (x.TmdbId > cursor.TmdbId || x.TmdbId == cursor.TmdbId && x.IsSeries && !cursor.IsSeries))
    };
    private static string MakeLibraryCursor(UserMovieState state, string sort, IReadOnlyCollection<Movie> movies)
    {
        var movie = movies.FirstOrDefault(x => x.TmdbId == state.TmdbId && x.IsSeries == state.IsSeries);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new LibraryCursor(state.UpdatedAt, state.Rating, movie?.Title, state.TmdbId, state.IsSeries))));
    }

    [HttpGet("movies")][AllowAnonymous]
    public async Task<IActionResult> Movies([FromQuery]string? cursor=null,[FromQuery]int limit=20,[FromQuery]int page=0,[FromQuery]string? q=null,[FromQuery]string? genreIds=null,[FromQuery]double? minRating=null,[FromQuery]int? yearFrom=null,[FromQuery]int? yearTo=null,[FromQuery]bool? isSeries=null,[FromQuery]string sort="popular",CancellationToken ct=default)
    {
        if (limit is < 1 or > 50) return ValidationProblem("Limit должен быть от 1 до 50");
        var after=ParseCatalogCursor(cursor);if(cursor is not null&&after is null)return ValidationProblem("Некорректный cursor");
        var genres = (genreIds ?? "").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(x=>int.TryParse(x,out var id)?id:0).Where(x=>x>0).Distinct().ToArray();
        var query=db.Movies.AsNoTracking().AsQueryable();
        if(isSeries is not null)query=query.Where(x=>x.IsSeries==isSeries);
        if(!string.IsNullOrWhiteSpace(q)){var term=q.Trim().ToLower();query=query.Where(x=>x.Title.ToLower().Contains(term)||(x.OriginalTitle!=null&&x.OriginalTitle.ToLower().Contains(term)));}
        if(genres.Length>0)query=query.Where(x=>x.MovieGenres.Any(g=>genres.Contains(g.GenreId)));
        if(minRating is not null)query=query.Where(x=>x.VoteAverage>=minRating&&x.VoteCount>0);
        if(yearFrom is not null)query=query.Where(x=>x.ReleaseDate!=null&&string.Compare(x.ReleaseDate,yearFrom+"-01-01")>=0);
        if(yearTo is not null)query=query.Where(x=>x.ReleaseDate!=null&&string.Compare(x.ReleaseDate,yearTo+"-12-31")<=0);
        var totalCount=await query.CountAsync(ct);
        if(after is not null)
        {
            var a=after;
            query=sort switch
            {
                "rating"=>query.Where(x=>x.VoteAverage<a.Number || x.VoteAverage==a.Number && (x.VoteCount<a.Votes || x.VoteCount==a.Votes && (x.TmdbId>a.Id || x.TmdbId==a.Id && x.IsSeries && !a.IsSeries))),
                "newest"=>query.Where(x=>string.Compare(x.ReleaseDate,a.Text)<0 || x.ReleaseDate==a.Text && (x.TmdbId>a.Id || x.TmdbId==a.Id && x.IsSeries && !a.IsSeries)),
                "oldest"=>query.Where(x=>string.Compare(x.ReleaseDate,a.Text)>0 || x.ReleaseDate==a.Text && (x.TmdbId>a.Id || x.TmdbId==a.Id && x.IsSeries && !a.IsSeries)),
                "title"=>query.Where(x=>string.Compare(x.Title,a.Text)>0 || x.Title==a.Text && (x.TmdbId>a.Id || x.TmdbId==a.Id && x.IsSeries && !a.IsSeries)),
                _=>query.Where(x=>x.Popularity<a.Number || x.Popularity==a.Number && (x.VoteCount<a.Votes || x.VoteCount==a.Votes && (x.TmdbId>a.Id || x.TmdbId==a.Id && x.IsSeries && !a.IsSeries)))
            };
        }
        query=sort switch {"rating"=>query.OrderByDescending(x=>x.VoteAverage).ThenByDescending(x=>x.VoteCount).ThenBy(x=>x.TmdbId).ThenBy(x=>x.IsSeries),"newest"=>query.OrderByDescending(x=>x.ReleaseDate).ThenBy(x=>x.TmdbId).ThenBy(x=>x.IsSeries),"oldest"=>query.OrderBy(x=>x.ReleaseDate).ThenBy(x=>x.TmdbId).ThenBy(x=>x.IsSeries),"title"=>query.OrderBy(x=>x.Title).ThenBy(x=>x.TmdbId).ThenBy(x=>x.IsSeries),_=>query.OrderByDescending(x=>x.Popularity).ThenByDescending(x=>x.VoteCount).ThenBy(x=>x.TmdbId).ThenBy(x=>x.IsSeries)};
        var rows=await LightweightMovies(query.Skip(page>1?(page-1)*limit:0).Take(limit+1)).ToListAsync(ct);var hasNext=rows.Count>limit;if(hasNext)rows.RemoveAt(rows.Count-1);
        var next=hasNext&&rows.Count>0?MakeCatalogCursor(rows[^1],sort):null;
        var items=rows.Select(MovieDto.Summary).ToArray();
        return Ok(new{items,totalCount,nextCursor=next,results=items,pageSize=limit});
    }
    [HttpGet("movies/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Movie(int id,[FromQuery]bool isSeries=false,CancellationToken ct=default)
    {
        var movie=await db.Movies.Include(x=>x.MovieGenres).ThenInclude(x=>x.Genre).SingleOrDefaultAsync(x=>x.TmdbId==id&&x.IsSeries==isSeries,ct);
        if(movie is null)return NotFound(new{message="Фильм или сериал не найден в каталоге"});
        if(movie.DetailsState!="ready"||movie.DetailsUpdatedAt<DateTime.UtcNow.AddDays(-30))
        {
            try{movie=await tmdb.Details(id,ct,isSeries);}
            catch(Exception ex)when(ex is HttpRequestException or JsonException)
            {
                // Summary всё ещё полезен: карточка покажет доступные данные и
                // enrichment-service повторит запрос с контролируемым retry.
            }
        }
        return Ok(MovieDto.Details(movie));
    }
    [HttpGet("recommendations")]
    [Authorize]
    public async Task<IActionResult> Recommendations([FromQuery]int page=1, CancellationToken ct=default)
    {
        if(page is <1 or >500)return ValidationProblem("Страница должна быть от 1 до 500");
        var ranked=await RankMovies(UserId,null,(page-1)*20,20,ct);
        return Ok(new{page,results=ranked.Select(MovieDto.Summary)});
    }
    [HttpPost("actions")]
    [Authorize]
    public async Task<IActionResult> Action(ActionRequest request,CancellationToken ct)
    {
        var allowed = new[] { "impression", "favorite", "unfavorite", "rate", "rating", "unrate", "swipe_left", "swipe_right", "not_interested", "watched", "unwatched" };
        if (request.TmdbId <= 0 || !allowed.Contains(request.ActionType, StringComparer.Ordinal))
            return ValidationProblem("Некорректный тип действия или идентификатор фильма");
        if (request.IdempotencyKey is null || request.IdempotencyKey.Length is < 1 or > 100)
            return ValidationProblem("IdempotencyKey должен содержать от 1 до 100 символов");
        if ((request.ActionType is "rate" or "rating") && (request.Value is null || request.Value < 1 || request.Value > 10))
            return ValidationProblem("Оценка должна быть от 1 до 10");

        var old = await db.UserActions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (old is not null) return Ok(new { id = old.Id, duplicate = true });
        if (!await db.Movies.AnyAsync(x => x.TmdbId == request.TmdbId && x.IsSeries == request.IsSeries, ct)) return NotFound(new { message = "Контент не найден в локальном каталоге" });

        var item = new UserAction { UserId = UserId, TmdbId = request.TmdbId, IsSeries=request.IsSeries, ActionType = request.ActionType, Value = request.Value, IdempotencyKey = request.IdempotencyKey };
        db.UserActions.Add(item);
        var state=await db.UserMovieStates.FindAsync([UserId,request.TmdbId,request.IsSeries],ct)??new UserMovieState{UserId=UserId,TmdbId=request.TmdbId,IsSeries=request.IsSeries};
        switch(request.ActionType){case "favorite":state.Favorite=true;state.PositiveSignals++;break;case "unfavorite":state.Favorite=false;break;case "watched":state.Watched=true;break;case "unwatched":state.Watched=false;break;case "rate":case "rating":state.Rating=request.Value;state.Watched=true;if(request.Value>=7)state.PositiveSignals++;else if(request.Value<=4)state.NegativeSignals++;break;case "unrate":state.Rating=null;break;case "swipe_right":state.Favorite=true;state.PositiveSignals++;break;case "swipe_left":case "not_interested":state.SuppressedUntil=DateTime.UtcNow.AddDays(7);state.NegativeSignals++;break;case "impression":state.LastImpressionAt=DateTime.UtcNow;break;}
        state.UpdatedAt=DateTime.UtcNow;if(db.Entry(state).State==EntityState.Detached)db.UserMovieStates.Add(state);
        db.OutboxEvents.Add(new OutboxEvent { Topic = "recommendations.action", Payload = JsonSerializer.Serialize(new { userId = UserId, tmdbId = request.TmdbId, action = request.ActionType }) });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var duplicate = await db.UserActions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (duplicate is not null) return Ok(new { id = duplicate.Id, duplicate = true });
            throw;
        }
        return Created("", new { id = item.Id, duplicate = false });
    }
    [HttpGet("library")][Authorize]
    public async Task<IActionResult> Library(CancellationToken ct)
    {
        var states=await db.UserMovieStates.AsNoTracking().Where(x=>x.UserId==UserId).OrderByDescending(x=>x.UpdatedAt).ToListAsync(ct);
        var ids=states.Select(x=>x.TmdbId).Distinct().ToArray();
        var movies=await LightweightMovies(db.Movies.AsNoTracking().Where(x=>ids.Contains(x.TmdbId))).ToListAsync(ct);
        return Ok(new{items=states.Select(x=>new{x.TmdbId,x.IsSeries,x.Rating,x.Favorite,x.Watched,x.SuppressedUntil,x.UpdatedAt,movie=movies.Where(m=>m.TmdbId==x.TmdbId&&m.IsSeries==x.IsSeries).Select(MovieDto.Summary).FirstOrDefault()})});
    }
    [HttpGet("reels")][AllowAnonymous]
    public async Task<IActionResult> Reels(CancellationToken ct)
    {
        var genreIds=ReelDefinitions.All.SelectMany(x=>x.Genres).Distinct().ToArray();
        var genres=await db.Genres.AsNoTracking().Where(x=>genreIds.Contains(x.TmdbId)).ToDictionaryAsync(x=>x.TmdbId,ct);
        // Reels only need summary columns. In particular, never read the potentially
        // multi-megabyte TMDB Payload for every catalog row.
        var candidates=await LightweightMovies(db.Movies.AsNoTracking()
            .Where(x=>x.BackdropPath!=null||x.PosterPath!=null)
            .OrderByDescending(x=>x.Popularity).ThenByDescending(x=>x.VoteCount).ThenBy(x=>x.TmdbId)
            .Take(ReelCandidateLimit)).ToListAsync(ct);
        var assigned=new HashSet<MovieKey>();
        var items=ReelDefinitions.All.Select(reel=>
        {
            var eligible=RankReelCandidates(candidates,reel);
            var representative=eligible.FirstOrDefault(x=>!assigned.Contains(new MovieKey(x.TmdbId,x.IsSeries)))??eligible.FirstOrDefault();
            if(representative is not null)assigned.Add(new MovieKey(representative.TmdbId,representative.IsSeries));
            return ReelMetadata(reel,genres,representative);
        }).ToArray();
        return Ok(new{items});
    }

    [HttpGet("reels/{slug}/feed")][AllowAnonymous]
    public async Task<IActionResult> ReelFeed(string slug,[FromQuery]string? cursor=null,[FromQuery]int limit=20,CancellationToken ct=default)
    {
        if(limit is <1 or >50)return ValidationProblem("Limit должен быть от 1 до 50");
        var reel=ReelDefinitions.All.FirstOrDefault(x=>x.Slug.Equals(slug,StringComparison.OrdinalIgnoreCase));if(reel is null)return NotFound();
        var parsed=FeedCursor(cursor);var sessionId=parsed.session??Guid.NewGuid().ToString("N");var offset=parsed.offset;
        var cacheKey=$"reel:v2:{User.FindFirstValue(ClaimTypes.NameIdentifier)??"guest"}:{slug}:{sessionId}";
        var cached=await cache.GetStringAsync(cacheKey,ct);List<MovieKey> keys;
        if(cached is null)
        {
            var uid=Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var value)?value:(Guid?)null;
            var ranked=await RankMovies(uid,reel,0,400,ct);keys=ranked.Select(x=>new MovieKey(x.TmdbId,x.IsSeries)).ToList();
            await cache.SetStringAsync(cacheKey,JsonSerializer.Serialize(keys),new DistributedCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(30)},ct);
        }
        else keys=JsonSerializer.Deserialize<List<MovieKey>>(cached)??[];
        var selected=keys.Skip(offset).Take(limit).ToArray();var rankedIds=keys.Select(x=>x.TmdbId).Distinct().ToArray();
        var coverKeys=(await db.Movies.AsNoTracking().Where(x=>rankedIds.Contains(x.TmdbId)&&(x.BackdropPath!=null||x.PosterPath!=null)).Select(x=>new{x.TmdbId,x.IsSeries}).ToListAsync(ct)).Select(x=>new MovieKey(x.TmdbId,x.IsSeries)).ToHashSet();
        var representativeKey=keys.FirstOrDefault(coverKeys.Contains);
        var selectedIds=selected.Append(representativeKey).Where(x=>x is not null).Select(x=>x!.TmdbId).Distinct().ToArray();
        var loaded=await LightweightMovies(db.Movies.AsNoTracking().Where(x=>selectedIds.Contains(x.TmdbId))).ToListAsync(ct);
        var items=selected.Select(k=>loaded.FirstOrDefault(x=>x.TmdbId==k.TmdbId&&x.IsSeries==k.IsSeries)).Where(x=>x is not null).Select(x=>MovieDto.Summary(x!)).ToArray();
        var representative=representativeKey is null?null:loaded.FirstOrDefault(x=>x.TmdbId==representativeKey.TmdbId&&x.IsSeries==representativeKey.IsSeries);
        var genreIds=reel.Genres.Distinct().ToArray();var genres=await db.Genres.AsNoTracking().Where(x=>genreIds.Contains(x.TmdbId)).ToDictionaryAsync(x=>x.TmdbId,ct);
        var next=offset+selected.Length<keys.Count?MakeFeedCursor(sessionId,offset+selected.Length):null;
        return Ok(new{reel=ReelMetadata(reel,genres,representative),feedSessionId=sessionId,items,nextCursor=next});
    }
    [HttpPost("imports")][Authorize] public async Task<IActionResult> StartImport(ImportRequest request,CancellationToken ct)
    {
        if (!Uri.TryCreate(request.ProfileUrl, UriKind.Absolute, out var profile) || profile.Scheme != Uri.UriSchemeHttps || !(profile.Host.Equals("kinopoisk.ru", StringComparison.OrdinalIgnoreCase) || profile.Host.EndsWith(".kinopoisk.ru", StringComparison.OrdinalIgnoreCase)))
            return ValidationProblem("Нужна HTTPS-ссылка на профиль kinopoisk.ru");
        var active = await db.ImportJobs.AsNoTracking().Where(x => x.UserId == UserId && x.ProfileUrl == request.ProfileUrl && (x.Status == "Queued" || x.Status == "Running" || x.Status == "Scraping" || x.Status == "Matching" || x.Status == "Applying" || x.Status == "WaitingForUser")).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (active is not null) return Conflict(new { id = active.Id, status = active.Status, message = "Импорт этого профиля уже выполняется" });
        var job=new ImportJob{UserId=UserId,ProfileUrl=request.ProfileUrl}; db.ImportJobs.Add(job); db.OutboxEvents.Add(new OutboxEvent{Topic="kinopoisk.import",Payload=JsonSerializer.Serialize(new{jobId=job.Id,userId=UserId,profileUrl=request.ProfileUrl})});
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var alreadyActive = await db.ImportJobs.AsNoTracking().AnyAsync(x => x.UserId == UserId && x.ProfileUrl == request.ProfileUrl && (x.Status == "Queued" || x.Status == "Running" || x.Status == "Scraping" || x.Status == "Matching" || x.Status == "Applying" || x.Status == "WaitingForUser"), ct);
            if (alreadyActive) return Conflict(new { message = "Импорт этого профиля уже выполняется" });
            throw;
        }
        return Accepted(new{id=job.Id,status=job.Status,progress=job.Progress});
    }
    [HttpGet("imports/{id:guid}")][Authorize] public async Task<IActionResult> Import(Guid id, CancellationToken ct)
    {
        var j = await db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (j is null) return NotFound();
        return Ok(new
        {
            id = j.Id,
            status = j.Status,
            progress = j.Progress,
            phase = j.Phase,
            phaseProgress = j.PhaseProgress,
            importedCount = j.ImportedCount,
            discoveredCount = j.DiscoveredCount,
            matchedCount = j.MatchedCount,
            appliedCount = j.AppliedCount,
            unmatchedCount = j.UnmatchedCount,
            pagesProcessed = j.PagesProcessed,
            pagesTotal = j.PagesTotal,
            estimatedRemainingSeconds = j.EstimatedRemainingSeconds,
            error = j.Error,
            captcha = j.Status == "WaitingForUser" ? TryCaptchaDetail(j.Checkpoint) : null,
            createdAt = j.CreatedAt,
            updatedAt = j.UpdatedAt,
        });
    }
    [HttpPost("imports/{id:guid}/resume")][Authorize] public async Task<IActionResult> ResumeImport(Guid id, CancellationToken ct)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (job is null) return NotFound();
        if (job.Status == "WaitingForUser")
        {
            var sessionId = TrySessionId(job.Checkpoint);
            if (sessionId is null) return Conflict(new { message = "Сессия CAPTCHA недоступна, запустите импорт заново" });
            db.OutboxEvents.Add(new OutboxEvent { Topic = "kinopoisk.import.resume", Payload = JsonSerializer.Serialize(new { jobId = job.Id, userId = UserId, profileUrl = job.ProfileUrl, sessionId }) });
        }
        else if (job.Status == "Failed")
        {
            if (!await db.ImportItems.AsNoTracking().AnyAsync(x => x.ImportJobId == job.Id, ct))
                return Conflict(new { message = "Нет сохранённых данных для продолжения, запустите импорт заново", status = job.Status });
            job.Error = null;
            db.OutboxEvents.Add(new OutboxEvent { Topic = "kinopoisk.import", Payload = JsonSerializer.Serialize(new { jobId = job.Id, userId = UserId, profileUrl = job.ProfileUrl }) });
        }
        else return Conflict(new { message = "Этот импорт нельзя продолжить", status = job.Status });
        job.Status = "Queued";
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Accepted(new { id = job.Id, status = job.Status, progress = job.Progress });
    }
    [HttpPost("imports/{id:guid}/cancel")][Authorize] public async Task<IActionResult> CancelImport(Guid id, CancellationToken ct)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (job is null) return NotFound();
        if (job.Status is "Completed" or "CompletedWithWarnings" or "Failed" or "Cancelled") return Ok(new { id = job.Id, status = job.Status });
        var sessionId = TrySessionId(job.Checkpoint);
        if (sessionId is not null)
        {
            try { await http.CreateClient("selenium").DeleteAsync($"/api/v1/kinopoisk/captcha/{Uri.EscapeDataString(sessionId)}", ct); }
            catch (HttpRequestException) { }
        }
        job.Status = "Cancelled";
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { id = job.Id, status = job.Status });
    }
    [HttpGet("imports/{id:guid}/items")][Authorize] public async Task<IActionResult> ImportItems(Guid id, CancellationToken ct) { var owns=await db.ImportJobs.AsNoTracking().AnyAsync(x=>x.Id==id&&x.UserId==UserId,ct); if(!owns)return NotFound(); var items=await db.ImportItems.AsNoTracking().Where(x=>x.ImportJobId==id).OrderBy(x=>x.Id).Select(x=>new{id=x.Id,externalId=x.ExternalId,title=x.Title,year=x.Year,genres=x.Genres,rating=x.Rating,kind=x.Kind,isSeries=x.IsSeries,kinopoiskUrl=x.KinopoiskUrl,page=x.Page,matchStatus=x.MatchStatus,tmdbId=x.TmdbId,matchError=x.MatchError}).ToListAsync(ct); return Ok(new{items}); }
    private static string? TrySessionId(string checkpoint)
    {
        try
        {
            using var doc = JsonDocument.Parse(checkpoint);
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.TryGetProperty("session_id", out var nested)) return nested.GetString();
            if (doc.RootElement.TryGetProperty("session_id", out var direct)) return direct.GetString();
        }
        catch (JsonException) { }
        return null;
    }
    private static object? TryCaptchaDetail(string checkpoint)
    {
        try
        {
            using var doc = JsonDocument.Parse(checkpoint);
            var root = doc.RootElement;
            var detail = root.TryGetProperty("detail", out var nested) ? nested : root;
            if (!detail.TryGetProperty("code", out var code) || code.GetString() != "CAPTCHA_REQUIRED") return null;
            string? StringProperty(string name) => detail.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
            int? IntProperty(string name) => detail.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : null;
            return new
            {
                code = code.GetString(),
                message = StringProperty("message"),
                pageUrl = StringProperty("page_url"),
                screenshotBase64 = StringProperty("screenshot_base64"),
                screenshotMimeType = StringProperty("screenshot_mime_type"),
                expiresInSeconds = IntProperty("expires_in_seconds"),
                action = StringProperty("action"),
                resumeEndpoint = StringProperty("resume_endpoint"),
                novncUrl = StringProperty("novnc_url"),
            };
        }
        catch (JsonException) { return null; }
    }
    private async Task<List<Movie>> RankMovies(Guid? userId, ReelDefinition? reel, int skip, int take, CancellationToken ct)
    {
        var now=DateTime.UtcNow;
        var states=userId is null?[]:await db.UserMovieStates.AsNoTracking().Where(x=>x.UserId==userId)
            .OrderByDescending(x=>x.UpdatedAt).Take(RankingCandidateLimit).ToListAsync(ct);
        var excluded=states.Where(x=>x.Watched||x.Rating!=null||x.SuppressedUntil>now).Select(x=>(x.TmdbId,x.IsSeries)).ToHashSet();
        var positive=states.Where(x=>x.Favorite||x.Rating>=7||x.PositiveSignals>0).ToArray();
        var positiveIds=positive.Select(x=>x.TmdbId).Distinct().ToArray();
        var liked=await LightweightMovies(db.Movies.AsNoTracking().Where(x=>positiveIds.Contains(x.TmdbId))).ToListAsync(ct);
        var lifetime=liked.SelectMany(x=>x.MovieGenres.Select(g=>g.GenreId)).GroupBy(x=>x).ToDictionary(x=>x.Key,x=>(double)x.Count());
        var recent=liked.SelectMany(movie=>
        {
            var state=positive.Where(x=>x.TmdbId==movie.TmdbId&&x.IsSeries==movie.IsSeries).OrderByDescending(x=>x.UpdatedAt).FirstOrDefault();
            var weight=state is null?0:Math.Pow(.5,Math.Max(0,(now-state.UpdatedAt).TotalDays)/30d);
            return movie.MovieGenres.Select(g=>(g.GenreId,weight));
        }).GroupBy(x=>x.GenreId).ToDictionary(x=>x.Key,x=>x.Sum(v=>v.weight));
        var query=db.Movies.AsNoTracking().AsQueryable();
        if(reel?.IsSeries is not null)query=query.Where(x=>x.IsSeries==reel.IsSeries);
        if(reel is not null&&reel.Genres.Length>0)query=query.Where(x=>x.MovieGenres.Any(g=>reel.Genres.Contains(g.GenreId)));
        if(reel?.MaxRuntime is not null)query=query.Where(x=>x.RuntimeMinutes!=null&&x.RuntimeMinutes<=reel.MaxRuntime);
        if(reel?.YearBefore is not null)query=query.Where(x=>x.ReleaseDate!=null&&string.Compare(x.ReleaseDate,reel.YearBefore+"-12-31")<=0);
        // Taste scoring is completed in memory, but only over a bounded, lightweight
        // server-side candidate pool. This keeps recommendations stable under large
        // detail payloads and prevents a single request from reading the whole table.
        var poolSize=Math.Min(RankingCandidateLimit,Math.Max(500,skip+take*10));
        var candidates=await LightweightMovies(query.OrderByDescending(x=>x.Popularity)
            .ThenByDescending(x=>x.VoteCount).ThenBy(x=>x.TmdbId).Take(poolSize)).ToListAsync(ct);
        double Taste(Movie m,Dictionary<int,double> profile)=>m.MovieGenres.Sum(x=>profile.GetValueOrDefault(x.GenreId));
        double Theme(Movie m)=>reel?.Strategy switch{"classic"=>(m.ReleaseDate is not null&&string.Compare(m.ReleaseDate,"2000-01-01")<0?25:0)+m.VoteAverage*3,"trending"=>m.Popularity*1.2,"underrated"=>m.VoteAverage*5-Math.Log10(Math.Max(10,m.VoteCount))*3,"visual"=>m.BackdropPath is null?0:20,"short"=>m.RuntimeMinutes is >0 and <=100?25:0,_=>reel is null?0:15};
        // Deterministic 10% exploration is mixed into each snapshot while the rest uses 70/30 lifetime/recent taste.
        return candidates.Where(x=>!excluded.Contains((x.TmdbId,x.IsSeries))).Select(x=>new{x,score=x.Popularity*.25+x.VoteAverage*2+Theme(x)+.7*Taste(x,lifetime)+.3*Taste(x,recent),explore=Math.Abs(HashCode.Combine(x.TmdbId,x.IsSeries))%10==0}).OrderByDescending(x=>x.explore).ThenByDescending(x=>x.score).ThenByDescending(x=>x.x.VoteCount).ThenBy(x=>x.x.TmdbId).Select(x=>x.x).Skip(skip).Take(take).ToList();
    }
    private static List<Movie> RankReelCandidates(IEnumerable<Movie> movies,ReelDefinition reel)
    {
        double Theme(Movie m)=>reel.Strategy switch{"classic"=>(m.ReleaseDate is not null&&string.Compare(m.ReleaseDate,"2000-01-01")<0?25:0)+m.VoteAverage*3,"trending"=>m.Popularity*1.2,"underrated"=>m.VoteAverage*5-Math.Log10(Math.Max(10,m.VoteCount))*3,"visual"=>m.BackdropPath is null?0:20,"short"=>m.RuntimeMinutes is >0 and <=100?25:0,_=>15};
        return movies.Where(m=>(reel.IsSeries is null||m.IsSeries==reel.IsSeries)
                &&(reel.Genres.Length==0||m.MovieGenres.Any(g=>reel.Genres.Contains(g.GenreId)))
                &&(reel.MaxRuntime is null||m.RuntimeMinutes is not null&&m.RuntimeMinutes<=reel.MaxRuntime)
                &&(reel.YearBefore is null||m.ReleaseDate is not null&&string.Compare(m.ReleaseDate,reel.YearBefore+"-12-31")<=0))
            .OrderByDescending(m=>m.Popularity*.25+m.VoteAverage*2+Theme(m)).ThenByDescending(m=>m.VoteCount).ThenBy(m=>m.TmdbId).ToList();
    }
    private const int ReelCandidateLimit=5000;
    private const int RankingCandidateLimit=5000;
    private static IQueryable<Movie> LightweightMovies(IQueryable<Movie> query)=>query.Select(movie=>new Movie
    {
        TmdbId=movie.TmdbId,IsSeries=movie.IsSeries,Title=movie.Title,OriginalTitle=movie.OriginalTitle,
        Tagline=movie.Tagline,Overview=movie.Overview,ReleaseDate=movie.ReleaseDate,
        RuntimeMinutes=movie.RuntimeMinutes,VoteAverage=movie.VoteAverage,VoteCount=movie.VoteCount,
        Popularity=movie.Popularity,PosterPath=movie.PosterPath,BackdropPath=movie.BackdropPath,
        DetailsState=movie.DetailsState,
        MovieGenres=movie.MovieGenres.Select(link=>new MovieGenre
        {
            TmdbId=link.TmdbId,IsSeries=link.IsSeries,GenreId=link.GenreId,
            Genre=new Genre{TmdbId=link.Genre.TmdbId,Slug=link.Genre.Slug,Name=link.Genre.Name,IsSeries=link.Genre.IsSeries}
        }).ToList()
    });
    private static object ReelMetadata(ReelDefinition reel,IReadOnlyDictionary<int,Genre> genres,Movie? representative)=>new
    {
        reel.Slug,reel.Title,reel.Description,reel.Strategy,isSeries=reel.IsSeries,
        genres=reel.Genres.Distinct().Where(genres.ContainsKey).Select(id=>genres[id]).Select(x=>new GenreDto(x.TmdbId,x.Slug,x.Name)).ToArray(),
        coverUrl=representative is null?null:MovieCover(representative),
        representativeMovie=representative is null?null:MovieDto.Summary(representative)
    };
    private static string? MovieCover(Movie movie)=>!string.IsNullOrWhiteSpace(movie.BackdropPath)?$"https://image.tmdb.org/t/p/original{movie.BackdropPath}":!string.IsNullOrWhiteSpace(movie.PosterPath)?$"https://image.tmdb.org/t/p/w500{movie.PosterPath}":null;
    private static string MakeCatalogCursor(Movie movie,string sort){var value=sort switch{"rating"=>movie.VoteAverage,"popular"=>movie.Popularity,_=>0};var text=sort switch{"title"=>movie.Title,"newest" or "oldest"=>movie.ReleaseDate,_=>null};return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CatalogCursor(value,movie.VoteCount,text,movie.TmdbId,movie.IsSeries))));}
    private static CatalogCursor? ParseCatalogCursor(string? cursor){if(string.IsNullOrWhiteSpace(cursor))return null;try{return JsonSerializer.Deserialize<CatalogCursor>(Encoding.UTF8.GetString(Convert.FromBase64String(cursor)));}catch{return null;}}
    private static string MakeFeedCursor(string session,int offset)=>Convert.ToBase64String(Encoding.UTF8.GetBytes($"{session}:{offset}"));
    private static (string? session,int offset) FeedCursor(string? cursor){if(string.IsNullOrWhiteSpace(cursor))return(null,0);try{var p=Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');return p.Length==2&&(int.TryParse(p[1],out var n))?(p[0],n):(null,-1);}catch{return(null,-1);}}
    private static IEnumerable<string> GenresFromPayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("genre_ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                return ids.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32().ToString()).ToArray();
            if (doc.RootElement.TryGetProperty("genres", out var genres) && genres.ValueKind == JsonValueKind.Array)
                return genres.EnumerateArray().Where(x => x.TryGetProperty("id", out _)).Select(x => x.GetProperty("id").GetInt32().ToString()).ToArray();
        }
        catch (JsonException) { }
        return Array.Empty<string>();
    }
}
public sealed record RegisterRequest(string Email,string Password,string? DisplayName); public sealed record LoginRequest(string Email,string Password); public sealed record ActionRequest(int TmdbId,string ActionType,double? Value,string? IdempotencyKey,bool IsSeries=false); public sealed record ImportRequest(string ProfileUrl);
public sealed record LibraryQuery(string? Cursor = null, int Limit = 20, int Page = 1, string? Q = null, string? GenreIds = null, double? MinRating = null, int? YearFrom = null, int? YearTo = null, bool? IsSeries = null, string? Sort = "recent");
public sealed record MovieKey(int TmdbId,bool IsSeries);
public sealed record CatalogCursor(double Number,int Votes,string? Text,int Id,bool IsSeries);
public sealed record ReelDefinition(string Slug,string Title,string Description,int[] Genres,string Strategy="genres",bool? IsSeries=false,int? MaxRuntime=null,int? YearBefore=null);
public static class ReelDefinitions
{
    public static readonly ReelDefinition[] All=
    [
        R("na-odnom-dyhanii","На одном дыхании","Затягивает с первых минут",53,80,9648),R("bez-tormozov","Без тормозов","Максимум экшена",28,53,12),R("nervy-na-predele","Нервы на пределе","Напряжённое кино",53,27),R("ne-smotri-odin","Не смотри один","Самое страшное",27,53),R("temnye-dela","Тёмные дела","Убийства и расследования",80,9648,53),R("slomai-mne-mozg","Сломай мне мозг","Запутанные сюжеты",878,53,9648),R("v-drugoi-mir","В другой мир","Магические миры",14,12),R("sredi-zvezd","Где-то среди звёзд","Космос и другие планеты",878,12),R("buduschee-zdes","Будущее уже здесь","ИИ, роботы, киберпанк",878,53),R("konec-sveta","Конец света","Апокалипсис и выживание",878,27,28),R("vyzhit","Выжить любой ценой","Борьба за жизнь",53,12,18),R("voennoe-kino","Военное кино","Войны и солдаты",10752,18,36),R("po-sledam-istorii","По следам истории","Реальные исторические эпохи",36,18),R("realnye-sobytiya","Основано на реальных событиях","Реальные истории",18,36,80),R("velikie-lyudi","Истории великих людей","Известные личности",18,36),R("dikii-zapad","Дикий Запад","Ковбои и фронтир",37,18,28),R("anime-vecher","Аниме-вечер","Полнометражное аниме",16,14,878),R("dlya-semi","Для всей семьи","Смотреть вместе",10751,35,12),R("animaciya-vzroslym","Мультфильмы не только детям","Сильная взрослая анимация",16,35,18),R("posmeyatsya","Просто посмеяться","Лёгкие комедии",35),R("chernyi-yumor","Чёрный юмор","Жёсткий и абсурдный юмор",35,80,18),R("hochu-vlubitsya","Хочу влюбиться","Лёгкая романтика",10749,35),R("lubov-isportila","Любовь всё испортила","Сложные отношения",10749,18),R("poplakat","Поплакать и отпустить","Эмоциональное кино",18,10749),R("teplyi-vecher","Тёплый вечер","Доброе comfort-кино",35,10751,18),R("bolshoe-kino","Большое кино","Эпичные блокбастеры",28,878,12),new("krasivo","Красиво до мурашек","Очень визуальное кино",[],"visual"),R("muzyka-gromche","Музыка громче","Музыканты и музыкальные фильмы",10402,18,35),R("sport","Спортивный характер","Победы и преодоление",18),R("documentary","Документалки, которые затягивают","Интересный нон-фикшн",99),new("must-see","Стоит увидеть каждому","Признанная классика",[],"classic"),new("provereno-vremenem","Проверено временем","Лучшее старое кино",[],"classic",false,null,2005),new("trending","Все сейчас смотрят","Тренды и популярное",[],"trending"),new("underrated","Ты мог это пропустить","Недооценённые фильмы",[],"underrated"),new("90-minut","У меня есть 90 минут","Хорошие короткие фильмы",[],"short",false,100),new("mini-serial","Мини-сериал на выходные","Короткие истории",[],"trending",true),new("serial-marafon","Сериальный марафон","Истории надолго",[],"popular",true),new("anime-serial","Аниме-сериал","Японская анимация",[16],"genres",true),new("serial-classic","Проверенные сериалы","Лучшее из сериалов",[],"classic",true)
    ];
    private static ReelDefinition R(string slug,string title,string description,params int[] genres)=>new(slug,title,description,genres);
}
