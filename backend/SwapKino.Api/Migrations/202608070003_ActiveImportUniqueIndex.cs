using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SwapKino.Api.Migrations;

[DbContext(typeof(SwapKinoDbContext))]
[Migration("202608070003_ActiveImportUniqueIndex")]
public partial class ActiveImportUniqueIndex : Migration
{
    protected override void Up(MigrationBuilder m) => m.Sql("CREATE UNIQUE INDEX \"IX_ImportJobs_Active_User_Profile\" ON \"ImportJobs\" (\"UserId\", \"ProfileUrl\") WHERE \"Status\" IN ('Queued', 'Running', 'WaitingForUser');");
    protected override void Down(MigrationBuilder m) => m.Sql("DROP INDEX IF EXISTS \"IX_ImportJobs_Active_User_Profile\";");
}
