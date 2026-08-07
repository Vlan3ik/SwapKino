using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using SwapKino.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
builder.Services.AddDbContext<SwapKinoDbContext>(o => o.UseNpgsql(config.GetConnectionString("Default") ?? config["DATABASE_URL"]));
builder.Services.AddIdentityCore<User>(options =>
{
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Password.RequiredLength = 8;
}).AddRoles<IdentityRole<Guid>>().AddSignInManager<SignInManager<User>>().AddEntityFrameworkStores<SwapKinoDbContext>();
var jwtSecret = config["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is required");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuerSigningKey=true, IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)), ValidateIssuer=false, ValidateAudience=false, ValidateLifetime=true, NameClaimType=ClaimTypes.NameIdentifier };
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/events"))
                context.Token = token;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});
var redisUrl = config["REDIS_URL"] ?? "redis-runtime:6379,abortConnect=false";
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisUrl));
builder.Services.AddSignalR().AddStackExchangeRedis(redisUrl);
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = config["REDIS_CACHE_URL"] ?? "redis-cache:6379,abortConnect=false"); builder.Services.AddHttpClient("tmdb", c => c.BaseAddress = new Uri((config["TMDB_BASE_URL"] ?? "https://api.themoviedb.org/3").TrimEnd('/') + "/")); builder.Services.AddHttpClient("selenium", c => c.BaseAddress = new Uri(config["SELENIUM_URL"] ?? "http://selenium-service:8081")); builder.Services.AddScoped<TmdbClient>(); builder.Services.AddHostedService<CatalogWarmupService>(); builder.Services.AddHostedService<EventsStreamRelay>(); builder.Services.AddControllers(); builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen();
var corsOrigins = config["CORS_ORIGINS"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? ["http://localhost:3000"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope()) { var db=scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>(); await db.Database.MigrateAsync(); }
app.UseSwagger(); app.UseSwaggerUI(); app.UseCors(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); app.MapHub<EventsHub>("/hubs/events");
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "api" }));
app.MapGet("/ready", async (SwapKinoDbContext db, IConnectionMultiplexer redis, CancellationToken ct) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        await redis.GetDatabase().PingAsync();
        if (redis.GetDatabase().StringGet("swapkino:worker:heartbeat").IsNullOrEmpty) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        return Results.Ok(new { status = "ready", database = "ok", redis = "ok", worker = "ok" });
    }
    catch { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
});
app.Run();

[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class EventsHub : Microsoft.AspNetCore.SignalR.Hub { public override async Task OnConnectedAsync() { var userId=Context.User?.FindFirstValue(ClaimTypes.NameIdentifier); if(userId is not null) await Groups.AddToGroupAsync(Context.ConnectionId,$"user:{userId}"); await base.OnConnectedAsync(); } }

public partial class Program { }
