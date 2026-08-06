using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace SwapKino.Api;
[ApiController]
[Route("api/v1")]
public sealed class ApiController(SwapKinoDbContext db, UserManager<User> users, IConfiguration config, TmdbClient tmdb) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Token(User user) { var key=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JWT_SECRET"]!)); var creds=new SigningCredentials(key,SecurityAlgorithms.HmacSha256); return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(claims:[new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Email,user.Email!)],expires:DateTime.UtcNow.AddDays(30),signingCredentials:creds)); }
    [HttpPost("auth/register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request) { if(await users.FindByEmailAsync(request.Email) is not null)return Conflict(new{message="Email already registered"}); var u=new User{UserName=request.Email,Email=request.Email,DisplayName=request.DisplayName}; var result=await users.CreateAsync(u,request.Password); if(!result.Succeeded)return BadRequest(result.Errors); return Created("",new{accessToken=Token(u),user=new{id=u.Id,email=u.Email,displayName=u.DisplayName}}); }
    [HttpPost("auth/login")]
    public async Task<IActionResult> Login(LoginRequest request) { var u=await users.FindByEmailAsync(request.Email) ?? await users.FindByNameAsync(request.Email); if(u is null||!await users.CheckPasswordAsync(u,request.Password))return Unauthorized(new{message="Invalid email or password"}); return Ok(new{accessToken=Token(u),user=new{id=u.Id,email=u.Email,displayName=u.DisplayName}}); }
    [HttpGet("auth/me")][Authorize]
    public async Task<IActionResult> Me() { var user=await users.FindByIdAsync(UserId.ToString()); return user is null?Unauthorized():Ok(new{id=user.Id,email=user.Email,displayName=user.DisplayName,createdAt=user.CreatedAt}); }
    [HttpGet("movies")]
    [AllowAnonymous]
    public async Task<IActionResult> Movies([FromQuery]int page=1,[FromQuery]string? q=null,CancellationToken ct=default) { var rows=await tmdb.Discover(page,q,ct); return Ok(new{page,results=rows.Select(MovieDto.From)}); }
    [HttpGet("movies/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Movie(int id,CancellationToken ct) { var movie=await db.Movies.FindAsync([id],ct); if(movie is null||movie.DetailsState!="ready")movie=await tmdb.Details(id,ct); return Ok(MovieDto.From(movie)); }
    [HttpGet("recommendations")]
    [Authorize]
    public async Task<IActionResult> Recommendations([FromQuery]int page=1, CancellationToken ct=default)
    {
        var excluded = db.UserActions.Where(x => x.UserId == UserId && (x.ActionType == "swipe_left" || x.ActionType == "not_interested" || x.ActionType == "watched")).Select(x => x.TmdbId);
        var rows = await db.Movies.AsNoTracking().Where(x => !excluded.Contains(x.TmdbId)).OrderByDescending(x => x.Popularity).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        if (rows.Count == 0)
        {
            await tmdb.Discover(page, null, ct);
            rows = await db.Movies.AsNoTracking().Where(x => !excluded.Contains(x.TmdbId)).OrderByDescending(x => x.Popularity).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        }
        return Ok(new { page, results = rows.Select(MovieDto.From) });
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
    [HttpPost("imports")][Authorize] public async Task<IActionResult> StartImport(ImportRequest request,CancellationToken ct) { var job=new ImportJob{UserId=UserId,ProfileUrl=request.ProfileUrl}; db.ImportJobs.Add(job); db.OutboxEvents.Add(new OutboxEvent{Topic="kinopoisk.import",Payload=JsonSerializer.Serialize(new{jobId=job.Id,userId=UserId,profileUrl=request.ProfileUrl})}); await db.SaveChangesAsync(ct); return Accepted(new{id=job.Id,status=job.Status,progress=job.Progress}); }
    [HttpGet("imports/{id:guid}")][Authorize] public async Task<IActionResult> Import(Guid id, CancellationToken ct) { var j=await db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id&&x.UserId==UserId, ct); return j is null?NotFound():Ok(new{id=j.Id,status=j.Status,progress=j.Progress,importedCount=j.ImportedCount,error=j.Error,createdAt=j.CreatedAt,updatedAt=j.UpdatedAt}); }
    [HttpGet("imports/{id:guid}/items")][Authorize] public async Task<IActionResult> ImportItems(Guid id, CancellationToken ct) { var owns=await db.ImportJobs.AsNoTracking().AnyAsync(x=>x.Id==id&&x.UserId==UserId,ct); if(!owns)return NotFound(); var items=await db.ImportItems.AsNoTracking().Where(x=>x.ImportJobId==id).OrderBy(x=>x.Id).Select(x=>new{id=x.Id,title=x.Title,year=x.Year,genres=x.Genres,rating=x.Rating,kind=x.Kind,kinopoiskUrl=x.KinopoiskUrl,page=x.Page,matchStatus=x.MatchStatus,tmdbId=x.TmdbId,matchError=x.MatchError}).ToListAsync(ct); return Ok(new{items}); }
}
public sealed record RegisterRequest(string Email,string Password,string? DisplayName); public sealed record LoginRequest(string Email,string Password); public sealed record ActionRequest(int TmdbId,string ActionType,double? Value,string? IdempotencyKey); public sealed record ImportRequest(string ProfileUrl);
