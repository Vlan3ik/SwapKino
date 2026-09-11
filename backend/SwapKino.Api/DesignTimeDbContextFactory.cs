using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SwapKino.Api;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SwapKinoDbContext>
{
    public SwapKinoDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? "Host=localhost;Database=swapkino;Username=postgres;Password=postgres";
        return new SwapKinoDbContext(new DbContextOptionsBuilder<SwapKinoDbContext>()
            .UseNpgsql(connection)
            .Options);
    }
}
