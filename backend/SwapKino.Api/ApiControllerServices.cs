using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using StackExchange.Redis;

namespace SwapKino.Api;

public sealed class ApiAuthServices(
    UserManager<User> users,
    SignInManager<User> signIn,
    IConfiguration config)
{
    public UserManager<User> Users { get; } = users;
    public SignInManager<User> SignIn { get; } = signIn;
    public IConfiguration Config { get; } = config;
}

public sealed class ApiExternalServices(
    IConnectionMultiplexer redis,
    IHttpClientFactory http,
    TmdbCardGateway tmdbCards,
    VibixClient vibix,
    AvatarStorage avatars,
    ProductRecommendationService productRecommendations)
{
    public IConnectionMultiplexer Redis { get; } = redis;
    public IHttpClientFactory Http { get; } = http;
    public TmdbCardGateway TmdbCards { get; } = tmdbCards;
    public VibixClient Vibix { get; } = vibix;
    public AvatarStorage Avatars { get; } = avatars;
    public ProductRecommendationService ProductRecommendations { get; } = productRecommendations;
}

public sealed record MovieQuery(
    string? Cursor = null,
    int Limit = 20,
    int Page = 1,
    string? Q = null,
    string? GenreIds = null,
    double? MinRating = null,
    int? YearFrom = null,
    int? YearTo = null,
    bool? IsSeries = null,
    string Sort = "popular");
