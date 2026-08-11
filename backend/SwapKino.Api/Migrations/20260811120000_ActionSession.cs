using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SwapKino.Api;

#nullable disable

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("20260811120000_ActionSession")]
public partial class ActionSession : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "SessionId", table: "UserActions", type: "text", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_UserActions_UserId_SessionId_CreatedAt", table: "UserActions", columns: new[] { "UserId", "SessionId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_UserActions_UserId_SessionId_CreatedAt", table: "UserActions");
        migrationBuilder.DropColumn(name: "SessionId", table: "UserActions");
    }
}
