using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.Widgets.Migrations.PostgreSql;

/// <summary>
/// Hand-written rather than scaffolded: a fixture's migrations are part of the test's fixed input, and a
/// regenerated timestamp would change the artifact this slice's determinism assertions compare.
/// </summary>
[DbContext(typeof(WidgetsPostgreSqlDbContext))]
[Migration("20260101000000_Initial")]
public partial class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: WidgetsDbContext.TableName,
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey($"PK_{WidgetsDbContext.TableName}", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: WidgetsDbContext.TableName);
}
