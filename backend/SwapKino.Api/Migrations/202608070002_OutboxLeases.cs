using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("202608070002_OutboxLeases")]
public partial class OutboxLeases : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<int>("AttemptCount", "OutboxEvents", type: "integer", nullable: false, defaultValue: 0);
        m.AddColumn<string>("LastError", "OutboxEvents", type: "text", nullable: true);
        m.AddColumn<DateTime?>("LockedUntil", "OutboxEvents", type: "timestamp with time zone", nullable: true);
        m.AddColumn<string>("LockedBy", "OutboxEvents", type: "character varying(100)", maxLength: 100, nullable: true);
        m.AddColumn<DateTime?>("NextAttemptAt", "OutboxEvents", type: "timestamp with time zone", nullable: true);
        m.AddColumn<DateTime?>("PublishedAt", "OutboxEvents", type: "timestamp with time zone", nullable: true);
        m.CreateIndex("IX_OutboxEvents_Lease", "OutboxEvents", new[] { "Published", "NextAttemptAt", "LockedUntil" });
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex("IX_OutboxEvents_Lease", "OutboxEvents");
        m.DropColumn("AttemptCount", "OutboxEvents"); m.DropColumn("LastError", "OutboxEvents"); m.DropColumn("LockedUntil", "OutboxEvents"); m.DropColumn("LockedBy", "OutboxEvents"); m.DropColumn("NextAttemptAt", "OutboxEvents"); m.DropColumn("PublishedAt", "OutboxEvents");
    }
}
