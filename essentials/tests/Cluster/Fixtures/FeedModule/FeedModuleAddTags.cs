using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Cluster.Fixtures.FeedModule;

/// <summary>
/// The next release's one migration, compiled only with <c>FeedModuleRelease=next</c>: a table nothing reads, so the only
/// thing it changes is whether the module has a migration pending.
/// </summary>
[DbContext(typeof(FeedModuleSqliteDbContext))]
[Migration(FeedModule.NextReleaseMigration)]
public sealed class FeedModuleAddTags : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: FeedModule.TagsTable,
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey($"PK_{FeedModule.TagsTable}", tag => tag.Id));

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(FeedModule.TagsTable);
}
