using System.Net;
using System.Net.Http.Json;
using Npgsql;
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
}

public sealed class SwapKinoApiFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> { }

[CollectionDefinition("api-integration", DisableParallelization = true)]
public sealed class ApiIntegrationCollection : ICollectionFixture<SwapKinoApiFactory> { }
