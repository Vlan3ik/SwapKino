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
using StackExchange.Redis;

namespace SwapKino.Api;
[ApiController]
[Route("api/v1")]
public sealed class ApiController(SwapKinoDbContext db, UserManager<User> users, SignInManager<User> signIn, IConfiguration config, IDistributedCache cache, IConnectionMultiplexer redis, IHttpClientFactory http, TmdbClient tmdb, VibixClient vibix, AvatarStorage avatars, RecommendationGateway gateway, ILogger<ApiController> log) : ControllerBase
{
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly int[] PsychologicalGenreIds = [18, 53, 9648, 27, 80];
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
        return StatusCode(statusCode, new { accessToken = Token(user), user = new { id = user.Id, email = user.Email, displayName = user.DisplayName, avatarUrl = user.AvatarUrl, createdAt = user.CreatedAt } });
    }
    [HttpPost("auth/register")][EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) { if (!request.PrivacyConsent) return BadRequest(new { message = "Необходимо согласие на обработку персональных данных" }); if(await users.FindByEmailAsync(request.Email) is not null)return Conflict(new{message="Email already registered"}); var u=new User{UserName=request.Email,Email=request.Email,DisplayName=request.DisplayName,PrivacyConsentAt=DateTime.UtcNow,PrivacyConsentVersion="2026-08-10"}; var result=await users.CreateAsync(u,request.Password); if(!result.Succeeded)return BadRequest(new { message = string.Join(" ", result.Errors.Select(error => error.Description)), errors = result.Errors }); await db.SaveChangesAsync(ct); return await AuthResponse(u, ct, StatusCodes.Status201Created); }
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
    public async Task<IActionResult> Me() { var user=await users.FindByIdAsync(UserId.ToString()); return user is null?Unauthorized():Ok(new{id=user.Id,email=user.Email,displayName=user.DisplayName,avatarUrl=user.AvatarUrl,createdAt=user.CreatedAt}); }

    [HttpPatch("profile")][Authorize]
    public async Task<IActionResult> UpdateProfile(ProfileUpdateRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null) return Unauthorized();
        var displayName = request.DisplayName?.Trim();
        if (displayName is not null && displayName.Length > 80) return BadRequest(new { message = "Имя не должно быть длиннее 80 символов" });
        if (request.AvatarUrl is not null)
        {
            var avatar = request.AvatarUrl.Trim();
            if (avatar.Length > 500) return BadRequest(new { message = "Ссылка на аватар слишком длинная" });
            if (avatar.Length > 0 && (!Uri.TryCreate(avatar, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
                return BadRequest(new { message = "Аватар должен быть ссылкой на изображение" });
            user.AvatarUrl = avatar.Length == 0 ? null : avatar;
        }
        if (displayName is not null) user.DisplayName = displayName.Length == 0 ? null : displayName;
        await db.SaveChangesAsync(ct);
        return Ok(new { id = user.Id, email = user.Email, displayName = user.DisplayName, avatarUrl = user.AvatarUrl, createdAt = user.CreatedAt });
    }

    [HttpPost("profile/avatar")][Authorize][RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> UploadAvatar(IFormFile? file, CancellationToken ct)
    {
        if (file is null) return BadRequest(new { message = "Выберите изображение" });
        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null) return Unauthorized();
        try
        {
            var oldAvatar = user.AvatarUrl;
            user.AvatarUrl = await avatars.SaveAsync(UserId, file, ct);
            await db.SaveChangesAsync(ct);
            await avatars.DeleteAsync(oldAvatar, ct);
            return Ok(new { id = user.Id, email = user.Email, displayName = user.DisplayName, avatarUrl = user.AvatarUrl, createdAt = user.CreatedAt });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("profile/avatar")][Authorize]
    public async Task<IActionResult> DeleteAvatar(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null) return Unauthorized();
        var oldAvatar = user.AvatarUrl;
        user.AvatarUrl = null;
        await db.SaveChangesAsync(ct);
        await avatars.DeleteAsync(oldAvatar, ct);
        return NoContent();
    }

    [HttpPost("auth/password")][Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword)) return BadRequest(new { message = "Заполните текущий и новый пароль" });
        if (request.NewPassword == request.CurrentPassword) return BadRequest(new { message = "Новый пароль должен отличаться от текущего" });
        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null) return Unauthorized();
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return BadRequest(new { message = string.Join(" ", result.Errors.Select(x => x.Description)), errors = result.Errors });
        await db.RefreshSessions.Where(x => x.UserId == UserId && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAt, DateTime.UtcNow), ct);
        return NoContent();
    }
    [HttpDelete("account")][Authorize]
    public async Task<IActionResult> DeleteAccount(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null) return Unauthorized();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.UserActions.RemoveRange(db.UserActions.Where(x => x.UserId == UserId));
        db.UserMovieStates.RemoveRange(db.UserMovieStates.Where(x => x.UserId == UserId));
        db.UserExternalItems.RemoveRange(db.UserExternalItems.Where(x => x.UserId == UserId));
        db.ImportJobs.RemoveRange(db.ImportJobs.Where(x => x.UserId == UserId));
        db.RefreshSessions.RemoveRange(db.RefreshSessions.Where(x => x.UserId == UserId));
        db.OutboxEvents.Add(new OutboxEvent { Topic = "recommendations.user.deleted", Payload = JsonSerializer.Serialize(new { userId = UserId }) });
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded) return Problem("Не удалось удалить аккаунт");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        Response.Cookies.Delete("swapkino-refresh", new CookieOptions { Path = "/api/v1/auth" });
        return NoContent();
    }
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
            user = new { id = user.Id, email = user.Email, displayName = user.DisplayName, avatarUrl = user.AvatarUrl, createdAt = user.CreatedAt },
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
    [HttpGet("movies/{id:int}/players")]
    [AllowAnonymous]
    public async Task<IActionResult> MoviePlayers(int id, [FromQuery] bool isSeries = false, CancellationToken ct = default)
    {
        var movie = await db.Movies.AsNoTracking().SingleOrDefaultAsync(x => x.TmdbId == id && x.IsSeries == isSeries, ct);
        if (movie is null) return NotFound(new { message = "Фильм или сериал не найден в каталоге" });
        if (movie.DetailsState != "ready")
        {
            try { movie = await tmdb.Details(id, ct, isSeries); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException) { }
        }
        try
        {
            var lookup = await vibix.FindAsync(movie, ct);
            var video = lookup.Video;
            var available = video is not null;
            return Ok(new { items = new[] { new { provider = "vibix", name = "Vibix", embedUrl = video?.IframeUrl, embed = video?.Embed, status = lookup.Status, available } } });
        }
        catch (HttpRequestException)
        {
            return Ok(new { items = new[] { new { provider = "vibix", name = "Vibix", embedUrl = (string?)null, embed = (VibixEmbed?)null, status = "upstream_error", available = false } } });
        }
    }
    [HttpGet("recommendations")]
    [Authorize]
    public async Task<IActionResult> Recommendations([FromQuery]int page=1, CancellationToken ct=default)
    {
        if(page is <1 or >500)return ValidationProblem("Страница должна быть от 1 до 500");
        IReadOnlyList<MovieKey> personalized = [];
        try { personalized = await gateway.GetRecommendationsAsync(UserId, "", Math.Min(100, page * 20), ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { }
        var keys = personalized.Skip((page - 1) * 20).Take(20).ToArray();
        if (keys.Length < 20)
            keys = await db.Movies.AsNoTracking().Where(x => !x.Adult && (x.PosterPath != null || x.BackdropPath != null)).OrderByDescending(x => x.Popularity).ThenByDescending(x => x.VoteCount).Skip((page - 1) * 20).Take(20).Select(x => new MovieKey(x.TmdbId, x.IsSeries)).ToArrayAsync(ct);
        var ids = keys.Select(x => x.TmdbId).ToArray();
        var movies = await LightweightMovies(db.Movies.AsNoTracking().Where(x => ids.Contains(x.TmdbId))).ToListAsync(ct);
        var results = keys.Select(key => movies.FirstOrDefault(x => x.TmdbId == key.TmdbId && x.IsSeries == key.IsSeries)).Where(x => x is not null).Select(x => MovieDto.Summary(x!));
        return Ok(new{page,results});
    }
    [HttpPost("actions")]
    [Authorize]
    public async Task<IActionResult> Action(ActionRequest request,CancellationToken ct)
    {
        var allowed = new[] { "impression", "favorite", "unfavorite", "rate", "rating", "unrate", "skip", "swipe_left", "swipe_right", "not_interested", "watched", "unwatched", "more_like_this", "less_like_this", "not_for_me", "already_watched", "rate_inline" };
        if (request.TmdbId <= 0 || !allowed.Contains(request.ActionType, StringComparer.Ordinal))
            return ValidationProblem("Некорректный тип действия или идентификатор фильма");
        if (request.IdempotencyKey is null || request.IdempotencyKey.Length is < 1 or > 100)
            return ValidationProblem("IdempotencyKey должен содержать от 1 до 100 символов");
        if ((request.ActionType is "rate" or "rating" or "rate_inline") && (request.Value is null || request.Value < 1 || request.Value > 10))
            return ValidationProblem("Оценка должна быть от 1 до 10");

        var old = await db.UserActions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (old is not null) return Ok(new { id = old.Id, duplicate = true });
        if (!await db.Movies.AnyAsync(x => x.TmdbId == request.TmdbId && x.IsSeries == request.IsSeries, ct)) return NotFound(new { message = "Контент не найден в локальном каталоге" });

        var item = new UserAction { UserId = UserId, TmdbId = request.TmdbId, IsSeries=request.IsSeries, ActionType = request.ActionType, Value = request.Value, IdempotencyKey = request.IdempotencyKey, SessionId = request.SessionId };
        db.UserActions.Add(item);
        var state=await db.UserMovieStates.FindAsync([UserId,request.TmdbId,request.IsSeries],ct)??new UserMovieState{UserId=UserId,TmdbId=request.TmdbId,IsSeries=request.IsSeries};
        switch(request.ActionType){case "favorite":state.Favorite=true;break;case "unfavorite":state.Favorite=false;break;case "watched":case "already_watched":state.Watched=true;break;case "unwatched":state.Watched=false;break;case "rate":case "rating":case "rate_inline":state.Rating=request.Value;state.Watched=true;break;case "unrate":state.Rating=null;break;case "skip":case "swipe_left":break;case "swipe_right":break;case "not_interested":case "not_for_me":case "less_like_this":state.SuppressedUntil=DateTime.UtcNow.AddDays(7);break;case "impression":state.LastImpressionAt=DateTime.UtcNow;break;}
        state.UpdatedAt=DateTime.UtcNow;if(db.Entry(state).State==EntityState.Detached)db.UserMovieStates.Add(state);
        if (request.ActionType == "impression")
            db.RecommendationImpressions.Add(new RecommendationImpression { UserId = UserId, TmdbId = request.TmdbId, IsSeries = request.IsSeries, ThemeId = request.ThemeId ?? "unknown", Position = request.Position ?? 0, Reason = "rendered", SessionId = request.SessionId });
        db.OutboxEvents.Add(new OutboxEvent { Topic = "recommendations.action", Payload = JsonSerializer.Serialize(new { userId = UserId, tmdbId = request.TmdbId, isSeries = request.IsSeries, action = request.ActionType, value = request.Value, sessionId = request.SessionId, createdAt = item.CreatedAt }) });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var duplicate = await db.UserActions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (duplicate is not null) return Ok(new { id = duplicate.Id, duplicate = true });
            throw;
        }
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var sessionDb = redis.GetDatabase();
            var sessionKey = $"rec:session:{UserId}:{request.SessionId}";
            var swipeCount = await sessionDb.HashIncrementAsync(sessionKey, "swipeCount", request.ActionType is "skip" or "swipe_left" or "swipe_right" ? 1 : 0);
            await sessionDb.HashSetAsync(sessionKey, [new HashEntry("userId", UserId.ToString()), new HashEntry("lastAction", request.ActionType), new HashEntry("lastMovieId", request.TmdbId)]);
            if (swipeCount > 0 && swipeCount % 15 == 0)
            {
                var profileVersion = await db.UserTasteProfiles.AsNoTracking().Where(x => x.UserId == UserId).Select(x => (int?)x.ProfileVersion).FirstOrDefaultAsync(ct) ?? 0;
                await sessionDb.HashSetAsync(sessionKey, [new HashEntry("refreshRequested", 1), new HashEntry("sessionProfileVersion", profileVersion), new HashEntry("lastRefreshAt", DateTime.UtcNow.ToString("O"))]);
            }
            await sessionDb.KeyExpireAsync(sessionKey, TimeSpan.FromHours(12));
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
        var viewerKey = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "guest";
        // v4 invalidates the former cache entries serialized with PascalCase fields.
        var reelsCacheKey = $"reels:v9:{viewerKey}";
        var cachedReels = await cache.GetStringAsync(reelsCacheKey, ct);
        if (!string.IsNullOrWhiteSpace(cachedReels)) return Content(cachedReels, "application/json");
        var genreIds=ReelDefinitions.All.SelectMany(x=>x.Genres).Distinct().ToArray();
        var genres=await db.Genres.AsNoTracking().Where(x=>genreIds.Contains(x.TmdbId)).ToDictionaryAsync(x=>x.TmdbId,ct);
        // Reels only need summary columns. In particular, never read the potentially
        // multi-megabyte TMDB Payload for every catalog row.
        var candidates=await LightweightMovies(db.Movies.AsNoTracking()
            .Where(x=>x.BackdropPath!=null||x.PosterPath!=null)
            .OrderByDescending(x=>x.Popularity).ThenByDescending(x=>x.VoteCount).ThenBy(x=>x.TmdbId)
            .Take(ReelCandidateLimit)).ToListAsync(ct);
        var thematicKeywordIds=(ReelDefinitions.All.First(x=>x.Slug=="sport").KeywordIds??[]).Concat(ReelDefinitions.All.First(x=>x.Slug=="psychological").KeywordIds??[]).Distinct().ToArray();
        var sportsCandidates=thematicKeywordIds.Length==0?[]:await LightweightMovies(db.Movies.AsNoTracking()
            .Where(x=>(x.BackdropPath!=null||x.PosterPath!=null)&&x.MovieKeywords.Any(k=>thematicKeywordIds.Contains(k.KeywordId)))
            .OrderByDescending(x=>x.Popularity).ThenByDescending(x=>x.VoteCount).ThenBy(x=>x.TmdbId)
            .Take(80)).ToListAsync(ct);
        var assigned=new HashSet<MovieKey>();
        var userId=Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var authenticatedUser)?authenticatedUser:(Guid?)null;
        // A guest has no taste profile yet. Rotate the representative covers in a
        // short, stable window so a refresh does not make the page jump, while the
        // front page does not always begin with exactly the same films either.
        var guestRotation=userId is null?DateTime.UtcNow.ToString("yyyyMMddHH"):null;
        var preferredGenres=userId is null?new Dictionary<int,double>():await db.UserMovieStates.AsNoTracking().Where(x=>x.UserId==userId&&(x.Rating>=7||x.Favorite)).Join(db.MovieGenres,m=>new{m.TmdbId,m.IsSeries},link=>new{link.TmdbId,link.IsSeries},(_,link)=>link.GenreId).GroupBy(x=>x).ToDictionaryAsync(x=>x.Key,x=>(double)x.Count(),ct);
        var orderedReels=ReelDefinitions.All.Select((reel,index)=>new{reel,index,score=reel.Genres.Sum(id=>preferredGenres.GetValueOrDefault(id))}).OrderByDescending(x=>x.score).ThenBy(x=>x.index).Select(x=>x.reel);
        var items=orderedReels.Select(reel=>
        {
            var eligible=RankReelCandidates(reel.Strategy is "sports" or "psychological"?candidates.Concat(sportsCandidates):candidates,reel);
            var representative=userId is null
                ? RotatingReelCandidate(eligible,assigned,$"{guestRotation}:{reel.Slug}")
                : eligible.FirstOrDefault(x=>!assigned.Contains(new MovieKey(x.TmdbId,x.IsSeries)))??eligible.FirstOrDefault();
            if(representative is not null)assigned.Add(new MovieKey(representative.TmdbId,representative.IsSeries));
            return ReelMetadata(reel,genres,representative);
        }).ToArray();
        var payload = JsonSerializer.Serialize(new { items }, CacheJsonOptions);
        await cache.SetStringAsync(reelsCacheKey, payload, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2)
        }, ct);
        return Content(payload, "application/json");
    }

    [HttpGet("reels/{slug}/feed")][AllowAnonymous]
    public async Task<IActionResult> ReelFeed(string slug,[FromQuery]string? cursor=null,[FromQuery]int limit=20,CancellationToken ct=default)
    {
        if(limit is <1 or >50)return ValidationProblem("Limit должен быть от 1 до 50");
        var reel=ReelDefinitions.All.FirstOrDefault(x=>x.Slug.Equals(slug,StringComparison.OrdinalIgnoreCase));if(reel is null)return NotFound();
        var parsed=FeedCursor(cursor);var sessionId=parsed.session??Guid.NewGuid().ToString("N");var offset=parsed.offset;
        var cacheKey=$"reel:v3:{User.FindFirstValue(ClaimTypes.NameIdentifier)??"guest"}:{slug}:{sessionId}";
        var cached=await cache.GetStringAsync(cacheKey,ct);List<MovieKey> keys;
        if(cached is null)
        {
            var uid=Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var value)?value:(Guid?)null;
            // Keep the first request bounded. The deck loads 20 cards and asks
            // for the next page with the cursor; ranking hundreds of graph-heavy
            // candidates before returning the first card caused 40-90s timeouts.
            keys=await BuildRecommendationDeck(uid, reel, sessionId, ct);
            await cache.SetStringAsync(cacheKey,JsonSerializer.Serialize(keys),new DistributedCacheEntryOptions{AbsoluteExpirationRelativeToNow=TimeSpan.FromMinutes(30)},ct);
        }
        else keys=JsonSerializer.Deserialize<List<MovieKey>>(cached)??[];
        var selected=keys.Skip(offset).Take(limit).ToArray();var rankedIds=keys.Select(x=>x.TmdbId).Distinct().ToArray();
        var coverKeys=(await db.Movies.AsNoTracking().Where(x=>rankedIds.Contains(x.TmdbId)&&(x.BackdropPath!=null||x.PosterPath!=null)).Select(x=>new{x.TmdbId,x.IsSeries}).ToListAsync(ct)).Select(x=>new MovieKey(x.TmdbId,x.IsSeries)).ToHashSet();
        var representativeKey=keys.FirstOrDefault(coverKeys.Contains);
        var selectedIds=selected.Append(representativeKey).Where(x=>x is not null).Select(x=>x!.TmdbId).Distinct().ToArray();
        var loaded=await LightweightMovies(db.Movies.AsNoTracking().Where(x=>selectedIds.Contains(x.TmdbId))).ToListAsync(ct);
        var items=selected.Select(k=>loaded.FirstOrDefault(x=>x.TmdbId==k.TmdbId&&x.IsSeries==k.IsSeries)).Where(x=>x is not null).Select(x=>MovieDto.Summary(x!)).ToArray();
        var feedItems=items.Select(movie=>new { kind="movie", movie }).ToArray();
        var representative=representativeKey is null?null:loaded.FirstOrDefault(x=>x.TmdbId==representativeKey.TmdbId&&x.IsSeries==representativeKey.IsSeries);
        var genreIds=reel.Genres.Distinct().ToArray();var genres=await db.Genres.AsNoTracking().Where(x=>genreIds.Contains(x.TmdbId)).ToDictionaryAsync(x=>x.TmdbId,ct);
        var next=offset+selected.Length<keys.Count?MakeFeedCursor(sessionId,offset+selected.Length):null;
        return Ok(new{reel=ReelMetadata(reel,genres,representative),feedSessionId=sessionId,items,feedItems,nextCursor=next});
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
    private async Task<List<MovieKey>> BuildRecommendationDeck(Guid? userId, ReelDefinition reel, string sessionId, CancellationToken ct)
    {
        var canonicalTheme = ThemeRegistry.CanonicalSlug(reel.Slug);
        var isRecommendationTheme = ThemeRegistry.Find(canonicalTheme) is not null;
        IReadOnlyList<MovieKey> requested = [];
        if (isRecommendationTheme)
        {
            try
            {
                requested = userId is Guid uid
                    ? await gateway.GetRecommendationsAsync(uid, canonicalTheme, 120, ct)
                    : await gateway.GetThemePopularAsync(canonicalTheme, 120, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Gorse unavailable for reel {Reel}; using local fallback", reel.Slug);
            }
        }
        var states = userId is Guid authenticated
            ? await db.UserMovieStates.AsNoTracking().Where(x => x.UserId == authenticated && (x.Watched || x.Rating != null || x.SuppressedUntil > DateTime.UtcNow)).Select(x => new MovieKey(x.TmdbId, x.IsSeries)).ToListAsync(ct)
            : [];
        var excluded = states.ToHashSet();
        if (userId is Guid sessionUser && !string.IsNullOrWhiteSpace(sessionId))
        {
            var sessionNegative = await redis.GetDatabase().SetMembersAsync($"rec:session:{sessionUser}:{sessionId}:negative-items");
            foreach (var value in sessionNegative)
            {
                var parts = value.ToString().Split(':');
                if (parts.Length == 3 && int.TryParse(parts[2], out var id)) excluded.Add(new MovieKey(id, parts[1] == "series"));
            }
        }
        var keys = requested.Where(x => !excluded.Contains(x)).Distinct().ToList();
        if (keys.Count < 20 && isRecommendationTheme)
        {
            var local = await db.MovieThemeMemberships.AsNoTracking()
                .Where(x => x.ThemeSlug == canonicalTheme && x.ThemeVersion == ThemeRegistry.Version)
                .OrderByDescending(x => x.Confidence)
                .Join(db.Movies.AsNoTracking(), membership => new { membership.TmdbId, membership.IsSeries }, movie => new { movie.TmdbId, movie.IsSeries }, (_, movie) => movie)
                .Where(x => !x.Adult && (x.PosterPath != null || x.BackdropPath != null))
                .OrderByDescending(x => x.Popularity).ThenByDescending(x => x.VoteCount).Take(120)
                .Select(x => new MovieKey(x.TmdbId, x.IsSeries)).ToListAsync(ct);
            keys.AddRange(local.Where(x => !excluded.Contains(x) && !keys.Contains(x)));
        }
        if (keys.Count < 20 && !isRecommendationTheme)
        {
            var legacy = await db.Movies.AsNoTracking()
                .Where(x => !x.Adult && (reel.IsSeries == null || x.IsSeries == reel.IsSeries) && (x.PosterPath != null || x.BackdropPath != null))
                .OrderByDescending(x => x.Popularity).ThenByDescending(x => x.VoteCount).Take(120)
                .Select(x => new MovieKey(x.TmdbId, x.IsSeries)).ToListAsync(ct);
            keys.AddRange(legacy.Where(x => !excluded.Contains(x) && !keys.Contains(x)));
        }
        return await ApplyHardGuards(keys, reel, ct);
    }

    private async Task<List<MovieKey>> ApplyHardGuards(IEnumerable<MovieKey> candidates, ReelDefinition reel, CancellationToken ct)
    {
        var ids = candidates.Select(x => x.TmdbId).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var query = db.Movies.AsNoTracking().Where(x => ids.Contains(x.TmdbId) && !x.Adult && (x.PosterPath != null || x.BackdropPath != null));
        if (reel.IsSeries is bool isSeries) query = query.Where(x => x.IsSeries == isSeries);
        if (reel.Genres.Length > 0) query = query.Where(x => x.MovieGenres.Any(g => reel.Genres.Contains(g.GenreId)));
        if (reel.KeywordIds is { Length: > 0 }) query = query.Where(x => x.MovieKeywords.Any(k => reel.KeywordIds.Contains(k.KeywordId)));
        if (reel.ExcludedGenres is { Length: > 0 }) query = query.Where(x => !x.MovieGenres.Any(g => reel.ExcludedGenres.Contains(g.GenreId)));
        if (reel.MaxRuntime is int maxRuntime) query = query.Where(x => x.RuntimeMinutes != null && x.RuntimeMinutes <= maxRuntime);
        if (reel.YearBefore is int yearBefore) query = query.Where(x => x.ReleaseDate != null && string.Compare(x.ReleaseDate, $"{yearBefore}-12-31") <= 0);
        if (reel.MinVoteAverage is double minVoteAverage) query = query.Where(x => x.VoteAverage >= minVoteAverage);
        if (reel.MinVoteCount is int minVoteCount) query = query.Where(x => x.VoteCount >= minVoteCount);
        if (reel.Strategy == "psychological") query = query.Where(x => x.MovieGenres.Any(g => ThemeRegistry.Find("psychological")!.RequiredGenres.Contains(g.GenreId)));
        var allowed = (await query.Select(x => new MovieKey(x.TmdbId, x.IsSeries)).ToListAsync(ct)).ToHashSet();
        return candidates.Where(allowed.Contains).Distinct().ToList();
    }
    private async Task<List<Movie>> RankMovies(Guid? userId, ReelDefinition? reel, int skip, int take, CancellationToken ct)
    {
        var now=DateTime.UtcNow;
        var states=userId is null?[]:await db.UserMovieStates.AsNoTracking().Where(x=>x.UserId==userId).Take(RankingCandidateLimit).ToArrayAsync(ct);
        var cachedProfile=userId is null?RedisValue.Null:await redis.GetDatabase().StringGetAsync($"{RecommendationProfileBuilder.ProfileCachePrefix}{userId}:profile");
        UserTasteProfile? storedProfile=null;
        if (!cachedProfile.IsNullOrEmpty)
        {
            try { storedProfile=JsonSerializer.Deserialize<UserTasteProfile>(cachedProfile!); } catch (JsonException) { }
        }
        storedProfile ??= userId is null?null:await db.UserTasteProfiles.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==userId,ct);
        var actions=storedProfile is null&&userId is not null?await db.UserActions.AsNoTracking().Where(x=>x.UserId==userId).OrderByDescending(x=>x.CreatedAt).Take(RankingCandidateLimit*3).ToArrayAsync(ct):[];
        var excluded=states.Where(x=>x.Watched||x.Rating!=null||x.SuppressedUntil>now).Select(x=>(x.TmdbId,x.IsSeries)).ToHashSet();
        var positiveGenre=new Dictionary<int,double>(); var negativeGenre=new Dictionary<int,double>();
        var positiveKeyword=new Dictionary<int,double>(); var negativeKeyword=new Dictionary<int,double>();
        var positivePerson=new Dictionary<int,double>(); var negativePerson=new Dictionary<int,double>();
        var profileVector=new float[RecommendationEmbeddings.Dimensions];
        Dictionary<int,double> Read(string json,string field)
        {
            try
            {
                var document=JsonSerializer.Deserialize<TasteProfileDocument>(json);
                var values=field switch { "genres"=>document?.Genres, "keywords"=>document?.Keywords, _=>document?.People };
                return values?.Where(x=>int.TryParse(x.Key,out _)).ToDictionary(x=>int.Parse(x.Key),x=>x.Value)??[];
            }
            catch(JsonException){return[];}
        }
        var profileMovies=new List<Movie>();
        var signalByMovie=new Dictionary<(int TmdbId,bool IsSeries),double>();
        if(storedProfile is not null)
        {
            var positive=JsonSerializer.Deserialize<TasteProfileDocument>(storedProfile.PositiveProfileJson)??new([],[],[],0);
            var negative=JsonSerializer.Deserialize<TasteProfileDocument>(storedProfile.NegativeProfileJson)??new([],[],[],0);
            foreach(var item in Read(storedProfile.PositiveProfileJson,"genres")) positiveGenre[item.Key]=item.Value;
            foreach(var item in Read(storedProfile.NegativeProfileJson,"genres")) negativeGenre[item.Key]=item.Value;
            foreach(var item in Read(storedProfile.PositiveProfileJson,"keywords")) positiveKeyword[item.Key]=item.Value;
            foreach(var item in Read(storedProfile.NegativeProfileJson,"keywords")) negativeKeyword[item.Key]=item.Value;
            foreach(var item in Read(storedProfile.PositiveProfileJson,"people")) positivePerson[item.Key]=item.Value;
            foreach(var item in Read(storedProfile.NegativeProfileJson,"people")) negativePerson[item.Key]=item.Value;
            profileVector=RecommendationEmbeddings.FromTasteProfiles(positive,negative);
        }
        else
        {
            var profileIds=actions.Select(x=>x.TmdbId).Distinct().ToArray();
            profileMovies=await LightweightMovies(db.Movies.AsNoTracking().Where(x=>profileIds.Contains(x.TmdbId))).ToListAsync(ct);
            signalByMovie=actions.GroupBy(x=>(x.TmdbId,x.IsSeries)).ToDictionary(x=>x.Key,x=>x.Sum(a=>ActionWeight(a.ActionType,a.Value)*Decay(a.CreatedAt, a.ActionType is not ("rate" or "rating" or "rate_inline"))));
            foreach(var movie in profileMovies)
            {
                var weight=signalByMovie.GetValueOrDefault((movie.TmdbId,movie.IsSeries));
                var genres=weight>=0?positiveGenre:negativeGenre; var keywords=weight>=0?positiveKeyword:negativeKeyword; var people=weight>=0?positivePerson:negativePerson;
                foreach(var feature in movie.MovieGenres) genres[feature.GenreId]=genres.GetValueOrDefault(feature.GenreId)+Math.Abs(weight)/Math.Max(1,movie.MovieGenres.Count);
                foreach(var feature in movie.MovieKeywords) keywords[feature.KeywordId]=keywords.GetValueOrDefault(feature.KeywordId)+Math.Abs(weight)/Math.Sqrt(Math.Max(1,movie.MovieKeywords.Count));
                foreach(var person in movie.MoviePeople.Where(x=>x.Department is "Director" or "Actor")) people[person.PersonId]=people.GetValueOrDefault(person.PersonId)+Math.Abs(weight)/(person.Department=="Director"?1:5);
            }
            profileVector=RecommendationEmbeddings.Average(profileMovies.Select(movie=>(movie,signalByMovie.GetValueOrDefault((movie.TmdbId,movie.IsSeries)))));
        }
        var query=db.Movies.AsNoTracking().AsQueryable();
        if(reel?.IsSeries is not null)query=query.Where(x=>x.IsSeries==reel.IsSeries);
        if(reel is not null&&reel.Genres.Length>0)query=query.Where(x=>x.MovieGenres.Any(g=>reel.Genres.Contains(g.GenreId)));
        if(reel?.PrimaryGenreId is int primaryGenre)query=query.Where(x=>x.MovieGenres.Any(g=>g.GenreId==primaryGenre));
        if(reel?.Strategy is "sports" or "psychological"&&reel.KeywordIds is { Length: > 0 })query=query.Where(x=>x.MovieKeywords.Any(k=>reel.KeywordIds.Contains(k.KeywordId)));
        if(reel?.Strategy is "psychological")query=query.Where(x=>x.MovieGenres.Any(g=>PsychologicalGenreIds.Contains(g.GenreId)));
        var excludedGenres=reel?.ExcludedGenres??[]; var themeKeywords=reel?.KeywordIds??[];
        if(excludedGenres.Length>0)query=query.Where(x=>!x.MovieGenres.Any(g=>excludedGenres.Contains(g.GenreId)));
        if(reel?.MaxRuntime is not null)query=query.Where(x=>x.RuntimeMinutes!=null&&x.RuntimeMinutes<=reel.MaxRuntime);
        if(reel?.YearBefore is not null)query=query.Where(x=>x.ReleaseDate!=null&&string.Compare(x.ReleaseDate,reel.YearBefore+"-12-31")<=0);
        if(reel?.MinVoteCount is not null)query=query.Where(x=>x.VoteCount>=reel.MinVoteCount);
        if(reel?.MinVoteAverage is not null)query=query.Where(x=>x.VoteAverage>=reel.MinVoteAverage);
        query=query.Where(x=>!x.Adult&&(x.PosterPath!=null||x.BackdropPath!=null));
        // Taste scoring is completed in memory, but only over a bounded, lightweight
        // server-side candidate pool. This keeps recommendations stable under large
        // detail payloads and prevents a single request from reading the whole table.
        // Для выдачи достаточно ограниченного пула: расширенный поиск здесь
        // блокировал первый экран и повторно пересчитывался для каждой темы.
        // The first screen only needs a compact, high-signal pool. Loading the
        // full keyword/person graph for hundreds of rows makes the API spend
        // seconds materializing a response before the deck can render.
        var poolSize=Math.Min(RankingCandidateLimit,Math.Max(120,skip+take*5));
        List<(int TmdbId, bool IsSeries)> annKeys=[];
        if (!profileVector.All(x=>x==0))
        {
            try
            {
                annKeys=await RecommendationEmbeddings.NearestAsync(db, profileVector, poolSize, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A broken/stale ANN index must not turn an otherwise valid themed feed into HTTP 500.
                // Continue with the bounded thematic candidate query below and repair the index asynchronously.
                log.LogWarning(ex, "ANN candidate retrieval failed for user {UserId}; using thematic fallback", userId);
            }
        }
        var annIds=annKeys.Select(x=>x.TmdbId).Distinct().ToArray();
        var personalized=userId is not null && actions.Length > 0;
        var candidateQuery=annIds.Length == 0 && !personalized
            ? query.OrderByDescending(x=>x.Popularity).ThenByDescending(x=>x.VoteCount).ThenBy(x=>x.TmdbId).Take(poolSize)
            : annIds.Length == 0
                ? query.OrderByDescending(x=>x.VoteCount).ThenByDescending(x=>x.VoteAverage).ThenBy(x=>x.TmdbId).Take(poolSize)
            : query.Where(x=>annIds.Contains(x.TmdbId)).Take(poolSize);
        var candidates=annIds.Length==0 && !personalized
            ? await LightweightMoviesColdStart(candidateQuery).ToListAsync(ct)
            : await LightweightMovies(candidateQuery.AsSplitQuery()).ToListAsync(ct);
        if (annIds.Length > 0 && candidates.Count < Math.Min(take, 20))
        {
            // ANN is global, while a reel is thematic. A small ANN pool can have
            // no usable items left after the reel filters and user exclusions.
            // Refill from the same themed query instead of returning an empty deck.
            log.LogInformation("Refilling {Reel} feed from thematic candidates after ANN pool was exhausted", reel?.Slug);
            candidates=await LightweightMovies(query.OrderByDescending(x=>x.VoteCount).ThenByDescending(x=>x.VoteAverage).ThenBy(x=>x.TmdbId).Take(poolSize).AsSplitQuery()).ToListAsync(ct);
        }
        double Match(Movie m)=>.55*SignedAverage(m.MovieGenres.Select(x=>positiveGenre.GetValueOrDefault(x.GenreId)),m.MovieGenres.Select(x=>negativeGenre.GetValueOrDefault(x.GenreId)))+.25*SignedAverage(m.MovieKeywords.Select(x=>positiveKeyword.GetValueOrDefault(x.KeywordId)),m.MovieKeywords.Select(x=>negativeKeyword.GetValueOrDefault(x.KeywordId)))+.15*SignedAverage(m.MoviePeople.Select(x=>positivePerson.GetValueOrDefault(x.PersonId)),m.MoviePeople.Select(x=>negativePerson.GetValueOrDefault(x.PersonId)))+.05*(m.OriginalLanguage is null?0:0.1);
        double Theme(Movie m)=>reel is null?0:reel.Strategy switch{"classic"=>(m.ReleaseDate is not null&&string.Compare(m.ReleaseDate,"2000-01-01")<0?1:0)+m.VoteAverage/10,"trending"=>Math.Min(1,m.Popularity/100),"underrated"=>Math.Clamp(m.VoteAverage/10-Math.Log10(Math.Max(10,m.VoteCount))/20,0,1),"short"=>m.RuntimeMinutes is >0 and <=100?1:0,"sports" or "psychological"=>Math.Min(1,m.MovieKeywords.Count(x=>themeKeywords.Contains(x.KeywordId))/2d),_=>.65*Average(m.MovieGenres.Where(x=>reel.Genres.Contains(x.GenreId)).Select(_=>1d))+.35*Average(m.MovieKeywords.Where(x=>themeKeywords.Contains(x.KeywordId)).Select(_=>1d))};
        double Quality(Movie m)=>Math.Clamp((m.VoteCount/(double)(m.VoteCount+200))*m.VoteAverage/10d+(200d/(m.VoteCount+200))*.65,0,1);
        var ranked=candidates.Where(x=>!excluded.Contains((x.TmdbId,x.IsSeries))).Select(x=>new{x,score=.42*Match(x)+.23*Theme(x)+.2*Quality(x)+.1*Math.Min(1,x.Popularity/100)+.05}).OrderByDescending(x=>x.score).ThenByDescending(x=>x.x.VoteCount).ThenBy(x=>x.x.TmdbId).ToList();
        var result=new List<Movie>();
        while(result.Count<skip+take && ranked.Count>0)
        {
            var next=ranked.OrderByDescending(candidate=>candidate.score-.20*result.Select(previous=>Jaccard(previous.MovieKeywords.Select(x=>x.KeywordId),candidate.x.MovieKeywords.Select(x=>x.KeywordId))).DefaultIfEmpty(0).Max()).ThenByDescending(candidate=>candidate.x.VoteCount).ThenBy(candidate=>candidate.x.TmdbId).First();
            result.Add(next.x); ranked.Remove(next);
        }
        return result.Skip(skip).Take(take).ToList();
    }
    private static double ActionWeight(string action,double? value)=>action switch{"favorite"=>2,"swipe_right"=>1,"more_like_this"=>2,"less_like_this"=>-2,"not_for_me"=>-3,"not_interested"=>-3,"rate" or "rating" or "rate_inline"=>value is >=9?3:value is 8?2:value is 7?1:value is <=5?-2:0,"impression"=>0,_=>0};
    private static double Decay(DateTime at,bool imported)=>imported?1:Math.Exp(-Math.Max(0,(DateTime.UtcNow-at).TotalDays)/180d);
    private static double Average(IEnumerable<double> values){var array=values.ToArray();return array.Length==0?0:Math.Clamp(array.Sum()/array.Length/3d,-1,1);}
    private static double SignedAverage(IEnumerable<double> positive,IEnumerable<double> negative){var p=Average(positive);var n=Average(negative);return Math.Clamp(p-n,-1,1);}
    private static double Jaccard(IEnumerable<int> a,IEnumerable<int> b){var left=a.ToHashSet();var right=b.ToHashSet();var union=left.Union(right).Count();return union==0?0:left.Intersect(right).Count()/(double)union;}
    private static List<Movie> RankReelCandidates(IEnumerable<Movie> movies,ReelDefinition reel)
    {
        var themeKeywords=reel.KeywordIds??[];
        double Theme(Movie m)=>reel.Strategy switch{"classic"=>(m.ReleaseDate is not null&&string.Compare(m.ReleaseDate,"2000-01-01")<0?25:0)+m.VoteAverage*3,"trending"=>m.Popularity*1.2,"underrated"=>m.VoteAverage*5-Math.Log10(Math.Max(10,m.VoteCount))*3,"visual"=>m.BackdropPath is null?0:20,"short"=>m.RuntimeMinutes is >0 and <=100?25:0,"sports" or "psychological"=>m.MovieKeywords.Count(x=>themeKeywords.Contains(x.KeywordId))*12,_=>15};
        return movies.Where(m=>(reel.IsSeries is null||m.IsSeries==reel.IsSeries)
                &&(reel.Genres.Length==0||m.MovieGenres.Any(g=>reel.Genres.Contains(g.GenreId)))
                &&(reel.PrimaryGenreId is null||m.MovieGenres.Any(g=>g.GenreId==reel.PrimaryGenreId))
                &&(reel.Strategy is not ("sports" or "psychological")||reel.KeywordIds is not { Length: > 0 }||m.MovieKeywords.Any(k=>reel.KeywordIds.Contains(k.KeywordId)))
                &&(reel.Strategy is not "psychological"||m.MovieGenres.Any(g=>PsychologicalGenreIds.Contains(g.GenreId)))
                &&(reel.MaxRuntime is null||m.RuntimeMinutes is not null&&m.RuntimeMinutes<=reel.MaxRuntime)
                &&(reel.YearBefore is null||m.ReleaseDate is not null&&string.Compare(m.ReleaseDate,reel.YearBefore+"-12-31")<=0))
            .OrderByDescending(m=>m.Popularity*.25+m.VoteAverage*2+Theme(m)).ThenByDescending(m=>m.VoteCount).ThenBy(m=>m.TmdbId).ToList();
    }
    private static Movie? RotatingReelCandidate(IReadOnlyList<Movie> candidates,IReadOnlySet<MovieKey> assigned,string rotationKey)
    {
        if(candidates.Count==0)return null;
        var window=Math.Min(candidates.Count,24);
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(rotationKey));
        var start=(int)(BitConverter.ToUInt32(hash,0)%(uint)window);
        for(var offset=0;offset<window;offset++)
        {
            var candidate=candidates[(start+offset)%window];
            if(!assigned.Contains(new MovieKey(candidate.TmdbId,candidate.IsSeries)))return candidate;
        }
        return candidates[start];
    }
    private const int ReelCandidateLimit=600;
    private const int RankingCandidateLimit=2000;
    private static IQueryable<Movie> LightweightMoviesColdStart(IQueryable<Movie> query)=>query.Select(movie=>new Movie
    {
        TmdbId=movie.TmdbId,IsSeries=movie.IsSeries,KinopoiskId=movie.KinopoiskId,ImdbId=movie.ImdbId,Adult=movie.Adult,Title=movie.Title,OriginalTitle=movie.OriginalTitle,
        Tagline=movie.Tagline,Overview=movie.Overview,ReleaseDate=movie.ReleaseDate,RuntimeMinutes=movie.RuntimeMinutes,VoteAverage=movie.VoteAverage,VoteCount=movie.VoteCount,
        Popularity=movie.Popularity,PosterPath=movie.PosterPath,BackdropPath=movie.BackdropPath,DetailsState=movie.DetailsState,
        MovieGenres=movie.MovieGenres.Select(link=>new MovieGenre
        {
            TmdbId=link.TmdbId,IsSeries=link.IsSeries,GenreId=link.GenreId,
            Genre=new Genre{TmdbId=link.Genre.TmdbId,Slug=link.Genre.Slug,Name=link.Genre.Name,IsSeries=link.Genre.IsSeries}
        }).ToList()
    });
    private static IQueryable<Movie> LightweightMovies(IQueryable<Movie> query)=>query.Select(movie=>new Movie
    {
        TmdbId=movie.TmdbId,IsSeries=movie.IsSeries,KinopoiskId=movie.KinopoiskId,ImdbId=movie.ImdbId,Adult=movie.Adult,Title=movie.Title,OriginalTitle=movie.OriginalTitle,
        Tagline=movie.Tagline,Overview=movie.Overview,ReleaseDate=movie.ReleaseDate,
        RuntimeMinutes=movie.RuntimeMinutes,VoteAverage=movie.VoteAverage,VoteCount=movie.VoteCount,
        Popularity=movie.Popularity,PosterPath=movie.PosterPath,BackdropPath=movie.BackdropPath,
        DetailsState=movie.DetailsState,
        MovieGenres=movie.MovieGenres.Select(link=>new MovieGenre
        {
            TmdbId=link.TmdbId,IsSeries=link.IsSeries,GenreId=link.GenreId,
            Genre=new Genre{TmdbId=link.Genre.TmdbId,Slug=link.Genre.Slug,Name=link.Genre.Name,IsSeries=link.Genre.IsSeries}
        }).ToList(),
        MovieKeywords=movie.MovieKeywords.Select(link=>new MovieKeyword
        {
            TmdbId=link.TmdbId,IsSeries=link.IsSeries,KeywordId=link.KeywordId,
            Keyword=new Keyword{TmdbId=link.Keyword.TmdbId,Slug=link.Keyword.Slug,Name=link.Keyword.Name}
        }).ToList(),
        MoviePeople=movie.MoviePeople.Select(person=>new MoviePerson{TmdbId=person.TmdbId,IsSeries=person.IsSeries,PersonId=person.PersonId,Name=person.Name,Department=person.Department,Character=person.Character,SortOrder=person.SortOrder}).ToList()
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
public sealed record RegisterRequest(string Email,string Password,string? DisplayName,bool PrivacyConsent); public sealed record LoginRequest(string Email,string Password); public sealed record ProfileUpdateRequest(string? DisplayName, string? AvatarUrl); public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword); public sealed record ActionRequest(int TmdbId,string ActionType,double? Value,string? IdempotencyKey,bool IsSeries=false,string? SessionId=null,string? ThemeId=null,int? Position=null); public sealed record ImportRequest(string ProfileUrl);
public sealed record LibraryQuery(string? Cursor = null, int Limit = 20, int Page = 1, string? Q = null, string? GenreIds = null, double? MinRating = null, int? YearFrom = null, int? YearTo = null, bool? IsSeries = null, string? Sort = "recent");
public sealed record MovieKey(int TmdbId,bool IsSeries);
public sealed record CatalogCursor(double Number,int Votes,string? Text,int Id,bool IsSeries);
public sealed record ReelDefinition(string Slug,string Title,string Description,int[] Genres,string Strategy="genres",bool? IsSeries=false,int? MaxRuntime=null,int? YearBefore=null,int[]? KeywordIds=null,int[]? ExcludedGenres=null,double? MinVoteAverage=5.8,int? MinVoteCount=100)
{
    public int? PrimaryGenreId { get; init; }
}
public static class ReelDefinitions
{
    public static readonly ReelDefinition[] All=
    [
        R("horror","Хоррор","Страшное кино на вечер",27,53),R("comedy-company","Комедия для компании","Смешно смотреть вместе",35),R("detective","Детективы","Расследования, тайны и неожиданные развязки",80,9648,53),R("drama","Сильная драма","Истории, которые остаются после финала",18),R("war","Военное кино","Войны, солдаты и исторические события",10752,18,36),R("romance","Романтика","Фильмы о любви и отношениях",10749,35,18),R("family","Семейный вечер","Подходит для совместного просмотра",10751,35,12),R("animation","Анимация","Мультфильмы для любого возраста",16),R("anime","Аниме","Японская анимация и особая драматургия",16,14,878),R("psychological","Психологическое кино","Сложные герои и напряжение внутри",18,53),R("short","Фильмы до 100 минут","Когда есть один свободный вечер",35,53,28),new("similar-to-favorites","Похоже на любимое","Персональная лента на основе твоих оценок",[],"personal"),
        R("na-odnom-dyhanii","На одном дыхании","Затягивает с первых минут",53,80,9648),R("bez-tormozov","Без тормозов","Максимум экшена",28,53,12),R("nervy-na-predele","Нервы на пределе","Напряжённое кино",53,27),R("ne-smotri-odin","Не смотри один","Самое страшное",27,53),R("temnye-dela","Тёмные дела","Убийства и расследования",80,9648,53),R("slomai-mne-mozg","Сломай мне мозг","Запутанные сюжеты",878,53,9648),R("v-drugoi-mir","В другой мир","Магические миры",14,12),R("sredi-zvezd","Где-то среди звёзд","Космос и другие планеты",878,12),R("buduschee-zdes","Будущее уже здесь","ИИ, роботы, киберпанк",878,53),R("konec-sveta","Конец света","Апокалипсис и выживание",878,27,28),R("vyzhit","Выжить любой ценой","Борьба за жизнь",53,12,18),R("voennoe-kino","Военное кино","Войны и солдаты",10752,18,36),R("po-sledam-istorii","По следам истории","Реальные исторические эпохи",36,18),R("realnye-sobytiya","Основано на реальных событиях","Реальные истории",18,36,80),R("velikie-lyudi","Истории великих людей","Известные личности",18,36),R("dikii-zapad","Дикий Запад","Ковбои и фронтир",37,18,28),R("anime-vecher","Аниме-вечер","Полнометражное аниме",16,14,878),R("dlya-semi","Для всей семьи","Смотреть вместе",10751,35,12),R("animaciya-vzroslym","Мультфильмы не только детям","Сильная взрослая анимация",16,35,18),R("posmeyatsya","Просто посмеяться","Лёгкие комедии",35),R("chernyi-yumor","Чёрный юмор","Жёсткий и абсурдный юмор",35,80,18),R("hochu-vlubitsya","Хочу влюбиться","Лёгкая романтика",10749,35),R("lubov-isportila","Любовь всё испортила","Сложные отношения",10749,18),R("poplakat","Поплакать и отпустить","Эмоциональное кино",18,10749),R("teplyi-vecher","Тёплый вечер","Доброе comfort-кино",35,10751,18),R("bolshoe-kino","Большое кино","Эпичные блокбастеры",28,878,12),new("krasivo","Красиво до мурашек","Очень визуальное кино",[],"visual"),R("muzyka-gromche","Музыка громче","Музыканты и музыкальные фильмы",10402,18,35),R("sport","Спортивный характер","Победы и преодоление",18),R("documentary","Документалки, которые затягивают","Интересный нон-фикшн",99),new("must-see","Стоит увидеть каждому","Признанная классика",[],"classic"),new("provereno-vremenem","Проверено временем","Лучшее старое кино",[],"classic",false,null,2005),new("trending","Все сейчас смотрят","Тренды и популярное",[],"trending"),new("underrated","Ты мог это пропустить","Недооценённые фильмы",[],"underrated"),new("90-minut","У меня есть 90 минут","Хорошие короткие фильмы",[],"short",false,100),new("mini-serial","Мини-сериал на выходные","Короткие истории",[],"trending",true),new("serial-marafon","Сериальный марафон","Истории надолго",[],"popular",true),new("anime-serial","Аниме-сериал","Японская анимация",[16],"genres",true),new("serial-classic","Проверенные сериалы","Лучшее из сериалов",[],"classic",true)
    ];
    private static ReelDefinition R(string slug,string title,string description,params int[] genres)=>new ReelDefinition(slug,title,description,genres,KeywordIds:ThemeKeywords is null ? [] : ThemeKeywords.GetValueOrDefault(slug,[])) { PrimaryGenreId=genres.FirstOrDefault() is var primary && primary>0 ? primary : null };
    private static readonly Dictionary<string,int[]> ThemeKeywords = new()
    {
        ["horror"]=[12377,9715,6152], ["comedy-company"]=[6054,9713], ["detective"]=[9826,10714,825],
        ["war"]=[1956,10683], ["sci-fi"]=[9882,9840,310], ["fantasy"]=[9882,10292],
        ["similar-to-favorites"]=[], ["ne-smotri-odin"]=[12377,6152], ["temnye-dela"]=[9826,10714]
    };
    static ReelDefinitions()
    {
        for(var i=0;i<All.Length;i++)
        {
            if(All[i].PrimaryGenreId is null && All[i].Strategy=="genres" && All[i].Genres.Length>0)
                All[i]=All[i] with { PrimaryGenreId=All[i].Genres[0] };
            if(ThemeKeywords.TryGetValue(All[i].Slug,out var keywords))
                All[i]=All[i] with { KeywordIds=keywords };
        }
        var psychologicalIndex=Array.FindIndex(All,x=>x.Slug=="psychological");
        All[psychologicalIndex]=All[psychologicalIndex] with { Genres=[], Strategy="psychological", KeywordIds=[362567,184312,226106,9678,373849,196767,4280,41329,197708,159315,1523,18062,11036,159278,305943,252708,339805,215579], PrimaryGenreId=null };
        var sportIndex=Array.FindIndex(All,x=>x.Slug=="sport");
        All[sportIndex]=All[sportIndex] with { Genres=[], Strategy="sports", KeywordIds=[6075,333328,161643,294708,190471,167882,2006,8635,6496,209476,13042,579,9571,242128,352822], PrimaryGenreId=null };
        var shortIndex=Array.FindIndex(All,x=>x.Slug=="short");
        All[shortIndex]=All[shortIndex] with { Genres=[], Strategy="short", MaxRuntime=100, PrimaryGenreId=null };
    }
}
