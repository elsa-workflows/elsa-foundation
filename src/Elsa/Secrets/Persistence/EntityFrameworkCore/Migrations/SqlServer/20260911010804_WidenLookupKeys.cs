using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.SqlServer;

/// <inheritdoc />
public partial class WidenLookupKeys : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "TypeNameLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            collation: "Latin1_General_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "StoreNameLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            collation: "Latin1_General_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64);

        migrationBuilder.AlterColumn<string>(
            name: "ScopeLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: true,
            collation: "Latin1_General_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64,
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "TypeNameLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldCollation: "Latin1_General_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "StoreNameLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldCollation: "Latin1_General_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "ScopeLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true,
            oldCollation: "Latin1_General_BIN2");
    }
}
