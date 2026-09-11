using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapKino.Api.Migrations;

[Migration("20260908150000_SeedTestAdmin")]
[DbContext(typeof(SwapKinoDbContext))]
public partial class SeedTestAdmin : Migration
{
    private static readonly Guid RoleId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = new("22222222-2222-2222-2222-222222222222");

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"INSERT INTO \"AspNetRoles\" (\"Id\", \"Name\", \"NormalizedName\", \"ConcurrencyStamp\") SELECT '{RoleId}', 'admin', 'ADMIN', '{RoleId}' WHERE NOT EXISTS (SELECT 1 FROM \"AspNetRoles\" WHERE \"NormalizedName\" = 'ADMIN') ON CONFLICT (\"Id\") DO NOTHING;");
        migrationBuilder.Sql($"INSERT INTO \"AspNetUsers\" (\"Id\", \"DisplayName\", \"CreatedAt\", \"PrivacyConsentAt\", \"PrivacyConsentVersion\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", \"PasswordHash\", \"SecurityStamp\", \"ConcurrencyStamp\", \"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") SELECT '{UserId}', 'Администратор', NOW(), NOW(), 'test', 'admin', 'ADMIN', 'admin', 'ADMIN', TRUE, 'AQAAAAMAAYagAAAAEKNFFhIqpMmxbX9fz0LpWMhKPKUK0zh7hvHLxk4ufaCIcqFllEBHwonwnlPpi7mj1w==', '{UserId}', '{UserId}', FALSE, FALSE, TRUE, 0 WHERE NOT EXISTS (SELECT 1 FROM \"AspNetUsers\" WHERE \"NormalizedUserName\" = 'ADMIN') ON CONFLICT (\"Id\") DO NOTHING;");
        migrationBuilder.Sql("INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") SELECT u.\"Id\", r.\"Id\" FROM \"AspNetUsers\" u CROSS JOIN \"AspNetRoles\" r WHERE u.\"NormalizedUserName\" = 'ADMIN' AND r.\"NormalizedName\" = 'ADMIN' ON CONFLICT (\"UserId\", \"RoleId\") DO NOTHING;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($"DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\" = '{UserId}' AND \"RoleId\" = '{RoleId}';");
        migrationBuilder.Sql($"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = '{UserId}';");
        migrationBuilder.Sql($"DELETE FROM \"AspNetRoles\" WHERE \"Id\" = '{RoleId}';");
    }
}
