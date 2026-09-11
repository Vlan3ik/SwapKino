using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Minio;
using StackExchange.Redis;

namespace SwapKino.Api;

internal static class ApiBootstrap
{
    public static void AddSwapKinoServices(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        AddDatabase(builder, config);
        AddIdentity(builder);
        AddAuthentication(builder, config);
        AddInfrastructure(builder, config);
        AddHttpClients(builder, config);
        AddMvc(builder);
        AddCors(builder, config);
    }

    private static void AddDatabase(WebApplicationBuilder builder, IConfiguration config)
    {
        builder.Services.AddDbContext<SwapKinoDbContext>(options => options.UseNpgsql(config.GetConnectionString("Default") is { Length: > 0 } connection ? connection : config["DATABASE_URL"]));
    }

    private static void AddIdentity(WebApplicationBuilder builder)
    {
        builder.Services.AddIdentityCore<User>(options =>
        {
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Password.RequiredLength = 8;
        }).AddRoles<IdentityRole<Guid>>().AddSignInManager<SignInManager<User>>().AddEntityFrameworkStores<SwapKinoDbContext>();
    }

    private static void AddAuthentication(WebApplicationBuilder builder, IConfiguration config)
    {
        var jwtSecret = config["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is required");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)), ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true, NameClaimType = ClaimTypes.NameIdentifier };
            options.Events = new JwtBearerEvents { OnMessageReceived = ReadHubToken };
        });
        builder.Services.AddAuthorization();
    }

    private static Task ReadHubToken(MessageReceivedContext context)
    {
        var token = context.Request.Query["access_token"];
        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/events")) context.Token = token;
        return Task.CompletedTask;
    }

    private static void AddInfrastructure(WebApplicationBuilder builder, IConfiguration config)
    {
        var redisUrl = config["REDIS_URL"] ?? "redis-runtime:6379,abortConnect=false";
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisUrl));
    builder.Services.AddScoped<ProductRecommendationService>();
    builder.Services.AddScoped<ApiAuthServices>();
    builder.Services.AddScoped<ApiExternalServices>();
        builder.Services.AddScoped<TmdbCardGateway>();
        builder.Services.AddSignalR().AddStackExchangeRedis(redisUrl);
        builder.Services.AddStackExchangeRedisCache(options => options.Configuration = config["REDIS_CACHE_URL"] ?? "redis-cache:6379,abortConnect=false");
        var minioEndpoint = config["MINIO_ENDPOINT"] ?? "minio:9000";
        builder.Services.AddSingleton<IMinioClient>(_ => new MinioClient().WithEndpoint(minioEndpoint).WithCredentials(config["MINIO_ACCESS_KEY"] ?? "minio", config["MINIO_SECRET_KEY"] ?? "minio-secret-change-me").WithSSL(config.GetValue("MINIO_USE_SSL", false)).Build());
        builder.Services.AddSingleton<AvatarStorage>();
        builder.Services.AddScoped<TmdbClient>();
        builder.Services.AddHostedService<EventsStreamRelay>();
        AddRateLimiting(builder);
    }

    private static void AddRateLimiting(WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
    }

    private static void AddHttpClients(WebApplicationBuilder builder, IConfiguration config)
    {
        builder.Services.AddHttpClient("tmdb", client => client.BaseAddress = new Uri((config["TMDB_BASE_URL"] ?? "https://api.themoviedb.org/3").TrimEnd('/') + "/"));
        builder.Services.AddHttpClient("selenium", client => client.BaseAddress = new Uri(config["SELENIUM_URL"] ?? "http://selenium-service:8081"));
        builder.Services.AddHttpClient<VibixClient>(client => client.BaseAddress = new Uri(config["VIBIX_BASE_URL"] ?? "https://vibix.org/")).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }

    private static void AddMvc(WebApplicationBuilder builder)
    {
        builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
    }

    private static void AddCors(WebApplicationBuilder builder, IConfiguration config)
    {
        var origins = config["CORS_ORIGINS"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? ["http://localhost:3000"];
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
    }

    public static async Task InitializeSwapKinoDatabase(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SwapKinoDbContext>();
        await db.Database.MigrateAsync();
        if (app.Configuration.GetValue("SEED_TEST_ADMIN", false)) await SeedTestAdmin(scope.ServiceProvider);
    }

    private static async Task SeedTestAdmin(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roleManager.RoleExistsAsync("admin")) await roleManager.CreateAsync(new IdentityRole<Guid>("admin"));
        var userManager = services.GetRequiredService<UserManager<User>>();
        var admin = await userManager.FindByNameAsync("admin");
        if (admin is null)
        {
            admin = new User { UserName = "admin", Email = "admin", EmailConfirmed = true, DisplayName = "Администратор", PrivacyConsentAt = DateTime.UtcNow, PrivacyConsentVersion = "test", PasswordHash = new PasswordHasher<User>().HashPassword(new User(), "131313") };
            var result = await userManager.CreateAsync(admin);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description)));
        }
        if (!await userManager.IsInRoleAsync(admin, "admin")) await userManager.AddToRoleAsync(admin, "admin");
    }

    public static void MapSwapKinoApplication(this WebApplication app)
    {
        app.UseSwagger(); app.UseSwaggerUI(); app.UseCors(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); app.MapHub<EventsHub>("/hubs/events");
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "api" }));
        app.MapGet("/ready", Ready);
    }

    private static async Task<IResult> Ready(SwapKinoDbContext db, IConnectionMultiplexer redis, CancellationToken ct)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct)) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            await redis.GetDatabase().PingAsync();
            if (redis.GetDatabase().StringGet("swapkino:worker:heartbeat").IsNullOrEmpty) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            return Results.Ok(new { status = "ready", database = "ok", redis = "ok", worker = "ok", recommendationEngine = "local" });
        }
        catch { return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
    }
}
