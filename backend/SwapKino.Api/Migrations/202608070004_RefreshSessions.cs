using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SwapKino.Api;

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("202608070004_RefreshSessions")]
public partial class RefreshSessions : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("RefreshSessions", t => new
        {
            Id = t.Column<Guid>(type: "uuid", nullable: false),
            UserId = t.Column<Guid>(type: "uuid", nullable: false),
            TokenHash = t.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            CreatedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            ExpiresAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            RevokedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: true)
        }, constraints: t =>
        {
            t.PrimaryKey("PK_RefreshSessions", x => x.Id);
            t.ForeignKey("FK_RefreshSessions_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_RefreshSessions_TokenHash", "RefreshSessions", "TokenHash", unique: true);
        m.CreateIndex("IX_RefreshSessions_UserId_ExpiresAt", "RefreshSessions", new[] { "UserId", "ExpiresAt" });
    }
    protected override void Down(MigrationBuilder m) => m.DropTable("RefreshSessions");
}
