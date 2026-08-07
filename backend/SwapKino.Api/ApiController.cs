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
public sealed class ApiController(SwapKinoDbContext db, UserManager<User> users, SignInManager<User> signIn, IConfiguration config, TmdbClient tmdb, IDistributedCache cache, IHttpClientFactory http) : ControllerBase
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
    [HttpGet("movies")]
    [AllowAnonymous]
    public async Task<IActionResult> Movies([FromQuery]int page=1,[FromQuery]string? q=null,CancellationToken ct=default)
    {
        if (page < 1 || page > 500) return ValidationProblem("Страница должна быть от 1 до 500");
        var key = $"movies:v3:{page}:{q?.Trim().ToLowerInvariant() ?? "_"}";
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null) return Content(cached, "application/json");
        var tmdbPage = await tmdb.DiscoverPage(page, q, ct);
        var rows = tmdbPage.Results;
        var normalizedQuery = q?.Trim();
        var totalCount = string.IsNullOrWhiteSpace(normalizedQuery) ? await db.Movies.AsNoTracking()
            .Where(x => string.IsNullOrWhiteSpace(normalizedQuery) || x.Title.ToLower().Contains(normalizedQuery!.ToLower()) || (x.OriginalTitle != null && x.OriginalTitle.ToLower().Contains(normalizedQuery!.ToLower())))
            .CountAsync(ct) : tmdbPage.TotalResults;
        var totalPages = string.IsNullOrWhiteSpace(normalizedQuery) ? Math.Max(1, (int)Math.Ceiling(totalCount / 20d)) : tmdbPage.TotalPages;
        var body = JsonSerializer.Serialize(new { page, pageSize = 20, totalCount, totalPages, hasNextPage = page < totalPages, results = rows.Select(MovieDto.From) });
        await cache.SetStringAsync(key, body, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);
        return Content(body, "application/json");
    }
    [HttpGet("movies/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Movie(int id,CancellationToken ct) { var movie=await db.Movies.FindAsync([id],ct); if(movie is null||movie.DetailsState!="ready")movie=await tmdb.Details(id,ct); return Ok(MovieDto.From(movie)); }
    [HttpGet("recommendations")]
    [Authorize]
    public async Task<IActionResult> Recommendations([FromQuery]int page=1, CancellationToken ct=default)
    {
        if (page < 1 || page > 500) return ValidationProblem("Страница должна быть от 1 до 500");
        var excluded = db.UserActions.Where(x => x.UserId == UserId && (x.ActionType == "swipe_left" || x.ActionType == "not_interested" || x.ActionType == "watched")).Select(x => x.TmdbId);
        var likedIds = await db.UserActions.AsNoTracking().Where(x => x.UserId == UserId && (x.ActionType == "favorite" || (x.ActionType == "rate" && x.Value >= 7))).Select(x => x.TmdbId).Distinct().ToListAsync(ct);
        var likedPayloads = await db.Movies.AsNoTracking().Where(x => likedIds.Contains(x.TmdbId)).Select(x => x.Payload).ToListAsync(ct);
        var preferredGenres = likedPayloads.SelectMany(GenresFromPayload).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = await db.Movies.AsNoTracking().Where(x => !excluded.Contains(x.TmdbId)).ToListAsync(ct);
        var ranked = candidates
            .Select(movie => new { movie, score = movie.Popularity * 0.65 + movie.VoteAverage * 3 + GenresFromPayload(movie.Payload).Count(preferredGenres.Contains) * 12 })
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.movie.VoteCount)
            .Skip((page - 1) * 20)
            .Take(20)
            .Select(x => x.movie)
            .ToList();
        if (ranked.Count == 0)
        {
            await tmdb.Discover(page, null, ct);
            candidates = await db.Movies.AsNoTracking().Where(x => !excluded.Contains(x.TmdbId)).ToListAsync(ct);
            ranked = candidates.Select(movie => new { movie, score = movie.Popularity * 0.65 + movie.VoteAverage * 3 + GenresFromPayload(movie.Payload).Count(preferredGenres.Contains) * 12 }).OrderByDescending(x => x.score).ThenByDescending(x => x.movie.VoteCount).Skip((page - 1) * 20).Take(20).Select(x => x.movie).ToList();
        }
        return Ok(new { page, results = ranked.Select(MovieDto.From) });
    }
    [HttpPost("actions")]
    [Authorize]
    public async Task<IActionResult> Action(ActionRequest request,CancellationToken ct)
    {
        var allowed = new[] { "favorite", "unfavorite", "rate", "unrate", "swipe_left", "not_interested", "watched" };
        if (request.TmdbId <= 0 || !allowed.Contains(request.ActionType, StringComparer.Ordinal))
            return ValidationProblem("Некорректный тип действия или идентификатор фильма");
        if (request.IdempotencyKey is null || request.IdempotencyKey.Length is < 1 or > 100)
            return ValidationProblem("IdempotencyKey должен содержать от 1 до 100 символов");
        if (request.ActionType == "rate" && (request.Value is null || request.Value < 1 || request.Value > 10))
            return ValidationProblem("Оценка должна быть от 1 до 10");

        var old = await db.UserActions.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (old is not null) return Ok(new { id = old.Id, duplicate = true });
        if (!await db.Movies.AnyAsync(x => x.TmdbId == request.TmdbId, ct))
        {
            try { await tmdb.Details(request.TmdbId, ct); }
            catch (HttpRequestException) { return NotFound(new { message = "Фильм TMDB не найден" }); }
        }

        var item = new UserAction { UserId = UserId, TmdbId = request.TmdbId, ActionType = request.ActionType, Value = request.Value, IdempotencyKey = request.IdempotencyKey };
        db.UserActions.Add(item);
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
        // Текущее состояние библиотеки вычисляется в PostgreSQL, а не через
        // загрузку всей истории действий в память приложения.
        var items = await db.UserActions.FromSqlInterpolated($@"
            SELECT ""Id"", ""UserId"", ""TmdbId"", ""ActionType"", ""Value"", ""IdempotencyKey"", ""CreatedAt""
            FROM (
                SELECT a.*, ROW_NUMBER() OVER (PARTITION BY a.""TmdbId"" ORDER BY a.""CreatedAt"" DESC) AS rn
                FROM ""UserActions"" a
                WHERE a.""UserId"" = {UserId}
            ) latest
            WHERE latest.rn = 1")
            .AsNoTracking()
            .Select(x => new { id = x.Id, tmdbId = x.TmdbId, action = x.ActionType, value = x.Value, createdAt = x.CreatedAt })
            .ToListAsync(ct);
        return Ok(new { items });
    }
    [HttpPost("imports")][Authorize] public async Task<IActionResult> StartImport(ImportRequest request,CancellationToken ct)
    {
        if (!Uri.TryCreate(request.ProfileUrl, UriKind.Absolute, out var profile) || profile.Scheme != Uri.UriSchemeHttps || !(profile.Host.Equals("kinopoisk.ru", StringComparison.OrdinalIgnoreCase) || profile.Host.EndsWith(".kinopoisk.ru", StringComparison.OrdinalIgnoreCase)))
            return ValidationProblem("Нужна HTTPS-ссылка на профиль kinopoisk.ru");
        var active = await db.ImportJobs.AsNoTracking().Where(x => x.UserId == UserId && x.ProfileUrl == request.ProfileUrl && (x.Status == "Queued" || x.Status == "Running" || x.Status == "WaitingForUser")).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (active is not null) return Conflict(new { id = active.Id, status = active.Status, message = "Импорт этого профиля уже выполняется" });
        var job=new ImportJob{UserId=UserId,ProfileUrl=request.ProfileUrl}; db.ImportJobs.Add(job); db.OutboxEvents.Add(new OutboxEvent{Topic="kinopoisk.import",Payload=JsonSerializer.Serialize(new{jobId=job.Id,userId=UserId,profileUrl=request.ProfileUrl})});
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var alreadyActive = await db.ImportJobs.AsNoTracking().AnyAsync(x => x.UserId == UserId && x.ProfileUrl == request.ProfileUrl && (x.Status == "Queued" || x.Status == "Running" || x.Status == "WaitingForUser"), ct);
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
            importedCount = j.ImportedCount,
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
        if (job.Status != "WaitingForUser") return Conflict(new { message = "Импорт не ожидает прохождения CAPTCHA", status = job.Status });
        var sessionId = TrySessionId(job.Checkpoint);
        if (sessionId is null) return Conflict(new { message = "Сессия CAPTCHA недоступна, запустите импорт заново" });
        job.Status = "Queued";
        job.UpdatedAt = DateTime.UtcNow;
        db.OutboxEvents.Add(new OutboxEvent { Topic = "kinopoisk.import.resume", Payload = JsonSerializer.Serialize(new { jobId = job.Id, userId = UserId, profileUrl = job.ProfileUrl, sessionId }) });
        await db.SaveChangesAsync(ct);
        return Accepted(new { id = job.Id, status = job.Status, progress = job.Progress });
    }
    [HttpPost("imports/{id:guid}/cancel")][Authorize] public async Task<IActionResult> CancelImport(Guid id, CancellationToken ct)
    {
        var job = await db.ImportJobs.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (job is null) return NotFound();
        if (job.Status is "Completed" or "Failed" or "Cancelled") return Ok(new { id = job.Id, status = job.Status });
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
    [HttpGet("imports/{id:guid}/items")][Authorize] public async Task<IActionResult> ImportItems(Guid id, CancellationToken ct) { var owns=await db.ImportJobs.AsNoTracking().AnyAsync(x=>x.Id==id&&x.UserId==UserId,ct); if(!owns)return NotFound(); var items=await db.ImportItems.AsNoTracking().Where(x=>x.ImportJobId==id).OrderBy(x=>x.Id).Select(x=>new{id=x.Id,title=x.Title,year=x.Year,genres=x.Genres,rating=x.Rating,kind=x.Kind,kinopoiskUrl=x.KinopoiskUrl,page=x.Page,matchStatus=x.MatchStatus,tmdbId=x.TmdbId,matchError=x.MatchError}).ToListAsync(ct); return Ok(new{items}); }
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
public sealed record RegisterRequest(string Email,string Password,string? DisplayName); public sealed record LoginRequest(string Email,string Password); public sealed record ActionRequest(int TmdbId,string ActionType,double? Value,string? IdempotencyKey); public sealed record ImportRequest(string ProfileUrl);
