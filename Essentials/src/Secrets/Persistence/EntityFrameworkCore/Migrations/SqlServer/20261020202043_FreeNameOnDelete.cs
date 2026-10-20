using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class FreeNameOnDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A soft delete frees the name: the key gains a tombstone, empty for the secret holding a name and the
            // secret's id once it is deleted. Every row this finds was unique by tenant and name under the old key, so
            // the live ones keep their name with an empty tombstone, and the deleted ones need only a tombstone that is
            // not empty. Their ids are inside the JSON payload, which a migration cannot read without raw SQL, and raw
            // SQL cannot follow a host's configured schema, so they all get one constant instead. It sorts below every
            // generated id, so a later deletion under the same name is the one a read reports.
            migrationBuilder.AddColumn<string>(
                name: "Tombstone",
                table: "elsa_secrets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                collation: "Latin1_General_100_BIN2");

            // Column types are named so the update needs no table lookup in the target model, which a host's configured
            // schema would otherwise send to a table of that schema the scaffolded model does not have.
            migrationBuilder.UpdateData(
                table: "elsa_secrets",
                keyColumns: new[] { "Status" },
                keyColumnTypes: new[] { "nvarchar(32)" },
                keyValues: new object[] { "deleted" },
                columns: new[] { "Tombstone" },
                columnTypes: new[] { "nvarchar(64)" },
                values: new object[] { "-deleted-before-reuse" });

            // The default goes again once those rows hold their tombstones: a row written without one from here on is
            // refused, not taken for the row holding its name.
            migrationBuilder.AlterColumn<string>(
                name: "Tombstone",
                table: "elsa_secrets",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldDefaultValue: "",
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.DropPrimaryKey(
                name: "PK_elsa_secrets",
                table: "elsa_secrets");

            migrationBuilder.AddPrimaryKey(
                name: "PK_elsa_secrets",
                table: "elsa_secrets",
                columns: new[] { "TenantId", "NormalizedName", "Tombstone" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails, rather than dropping history, once a name has been reused: the old key cannot hold a deleted
            // secret and its successor side by side.
            migrationBuilder.DropPrimaryKey(
                name: "PK_elsa_secrets",
                table: "elsa_secrets");

            migrationBuilder.DropColumn(
                name: "Tombstone",
                table: "elsa_secrets");

            migrationBuilder.AddPrimaryKey(
                name: "PK_elsa_secrets",
                table: "elsa_secrets",
                columns: new[] { "TenantId", "NormalizedName" });
        }
    }
}
