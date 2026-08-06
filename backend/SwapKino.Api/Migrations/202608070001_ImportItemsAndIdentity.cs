using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("202608070001_ImportItemsAndIdentity")]
public partial class ImportItemsAndIdentity : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("AspNetRoleClaims", t => new
        {
            Id = t.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            RoleId = t.Column<Guid>(type: "uuid", nullable: false),
            ClaimType = t.Column<string>(type: "text", nullable: true),
            ClaimValue = t.Column<string>(type: "text", nullable: true),
        }, constraints: c => { c.PrimaryKey("PK_AspNetRoleClaims", x => x.Id); c.ForeignKey("FK_AspNetRoleClaims_AspNetRoles_RoleId", x => x.RoleId, "AspNetRoles", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("AspNetUserClaims", t => new
        {
            Id = t.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
            UserId = t.Column<Guid>(type: "uuid", nullable: false),
            ClaimType = t.Column<string>(type: "text", nullable: true),
            ClaimValue = t.Column<string>(type: "text", nullable: true),
        }, constraints: c => { c.PrimaryKey("PK_AspNetUserClaims", x => x.Id); c.ForeignKey("FK_AspNetUserClaims_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("AspNetUserLogins", t => new
        {
            LoginProvider = t.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            ProviderKey = t.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            ProviderDisplayName = t.Column<string>(type: "text", nullable: true),
            UserId = t.Column<Guid>(type: "uuid", nullable: false),
        }, constraints: c => { c.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey }); c.ForeignKey("FK_AspNetUserLogins_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("AspNetUserRoles", t => new
        {
            UserId = t.Column<Guid>(type: "uuid", nullable: false),
            RoleId = t.Column<Guid>(type: "uuid", nullable: false),
        }, constraints: c => { c.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId }); c.ForeignKey("FK_AspNetUserRoles_AspNetRoles_RoleId", x => x.RoleId, "AspNetRoles", "Id", onDelete: ReferentialAction.Cascade); c.ForeignKey("FK_AspNetUserRoles_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("AspNetUserTokens", t => new
        {
            UserId = t.Column<Guid>(type: "uuid", nullable: false),
            LoginProvider = t.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Name = t.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
            Value = t.Column<string>(type: "text", nullable: true),
        }, constraints: c => { c.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name }); c.ForeignKey("FK_AspNetUserTokens_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("ImportItems", t => new
        {
            Id = t.Column<Guid>(type: "uuid", nullable: false),
            ImportJobId = t.Column<Guid>(type: "uuid", nullable: false),
            KinopoiskUrl = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
            Title = t.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
            Year = t.Column<int>(type: "integer", nullable: true),
            Genres = t.Column<string>(type: "text", nullable: true),
            Rating = t.Column<double>(type: "double precision", nullable: true),
            Kind = t.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
            Page = t.Column<int>(type: "integer", nullable: false),
            MatchStatus = t.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
            TmdbId = t.Column<int>(type: "integer", nullable: true),
            MatchError = t.Column<string>(type: "text", nullable: true),
            CreatedAt = t.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        }, constraints: c => { c.PrimaryKey("PK_ImportItems", x => x.Id); c.ForeignKey("FK_ImportItems_ImportJobs_ImportJobId", x => x.ImportJobId, "ImportJobs", "Id", onDelete: ReferentialAction.Cascade); });
        m.CreateIndex("IX_AspNetRoleClaims_RoleId", "AspNetRoleClaims", "RoleId");
        m.CreateIndex("IX_AspNetUserClaims_UserId", "AspNetUserClaims", "UserId");
        m.CreateIndex("IX_AspNetUserLogins_UserId", "AspNetUserLogins", "UserId");
        m.CreateIndex("IX_AspNetUserRoles_RoleId", "AspNetUserRoles", "RoleId");
        m.CreateIndex("IX_ImportItems_ImportJobId_KinopoiskUrl", "ImportItems", new[] { "ImportJobId", "KinopoiskUrl" }, unique: true);
        m.CreateIndex("IX_ImportItems_ImportJobId_MatchStatus", "ImportItems", new[] { "ImportJobId", "MatchStatus" });
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("ImportItems"); m.DropTable("AspNetUserTokens"); m.DropTable("AspNetUserRoles"); m.DropTable("AspNetUserLogins"); m.DropTable("AspNetUserClaims"); m.DropTable("AspNetRoleClaims");
    }
}
