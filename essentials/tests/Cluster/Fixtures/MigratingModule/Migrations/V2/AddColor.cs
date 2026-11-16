using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Cluster.Fixtures.MigratingModule.Migrations;

/// <summary>Generation 2's migration, which only adds, so a host that has not yet run the new package still reads the table.</summary>
[DbContext(typeof(MigratingModuleSqliteDbContext))]
[Migration(MigratingModule.AddColor)]
public sealed class AddColor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: MigratingModule.AddedColumn, table: MigratingModule.Table, type: "TEXT", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: MigratingModule.AddedColumn, table: MigratingModule.Table);
}
