using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwapKino.Api.Migrations;

[Migration("20260814130000_RecommendationActionSync")]
[DbContext(typeof(SwapKinoDbContext))]
public partial class RecommendationActionSync : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<DateTime>(
        name: "RecommendationSyncedAt", table: "UserActions", type: "timestamp with time zone", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "RecommendationSyncedAt", table: "UserActions");
}
