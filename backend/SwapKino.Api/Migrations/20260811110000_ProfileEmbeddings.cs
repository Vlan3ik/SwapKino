using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SwapKino.Api;

#nullable disable

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("20260811110000_ProfileEmbeddings")]
public partial class ProfileEmbeddings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "PositiveEmbeddingJson", table: "UserTasteProfiles", type: "text", nullable: false, defaultValue: "[]");
        migrationBuilder.AddColumn<string>(name: "NegativeEmbeddingJson", table: "UserTasteProfiles", type: "text", nullable: false, defaultValue: "[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PositiveEmbeddingJson", table: "UserTasteProfiles");
        migrationBuilder.DropColumn(name: "NegativeEmbeddingJson", table: "UserTasteProfiles");
    }
}
