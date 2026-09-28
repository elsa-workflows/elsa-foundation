using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class SchemaVersionStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every row this finds was written before the stamp existed, by a build that wrote the 1.0.0 content
            // format, so the column arrives holding that version rather than empty.
            migrationBuilder.AddColumn<string>(
                name: "SchemaVersion",
                table: "elsa_secrets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "1.0.0");

            // The default goes again once those rows hold it: a row written without a stamp from here on is refused,
            // not read as any version.
            migrationBuilder.AlterColumn<string>(
                name: "SchemaVersion",
                table: "elsa_secrets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldDefaultValue: "1.0.0");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_secrets_SchemaVersion",
                table: "elsa_secrets",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elsa_secrets_SchemaVersion",
                table: "elsa_secrets");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "elsa_secrets");
        }
    }
}
