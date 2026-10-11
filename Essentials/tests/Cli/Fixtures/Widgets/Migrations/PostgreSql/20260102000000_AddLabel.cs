using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Acme.Widgets.Migrations.PostgreSql;

/// <summary>
/// A second migration, so a committed artifact can be aged by one migration: that is the difference
/// between "a statement was edited" and "the module changed since this file was generated", and
/// <c>script-check</c> must report those two distinctly.
/// </summary>
[DbContext(typeof(WidgetsPostgreSqlDbContext))]
[Migration("20260102000000_AddLabel")]
public partial class AddLabel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "Label",
            table: WidgetsDbContext.TableName,
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Label", table: WidgetsDbContext.TableName);
}
