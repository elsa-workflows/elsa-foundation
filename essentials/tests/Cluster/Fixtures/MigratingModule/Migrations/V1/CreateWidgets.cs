using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Cluster.Fixtures.MigratingModule.Migrations;

/// <summary>Generation 1's one migration: the table the module keeps.</summary>
[DbContext(typeof(MigratingModuleSqliteDbContext))]
[Migration(MigratingModule.CreateWidgets)]
public sealed class CreateWidgets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: MigratingModule.Table,
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey($"PK_{MigratingModule.Table}", widget => widget.Id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(MigratingModule.Table);
}
