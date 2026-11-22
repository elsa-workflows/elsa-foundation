using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.PostgreSql;

/// <inheritdoc />
public partial class OrdinalCollation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "TypeNameLookupKey",
            table: "elsa_secrets",
            type: "text",
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "StoreNameLookupKey",
            table: "elsa_secrets",
            type: "text",
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "elsa_secrets",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32);

        migrationBuilder.AlterColumn<string>(
            name: "ScopeLookupKey",
            table: "elsa_secrets",
            type: "text",
            nullable: true,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "NameSearchKey",
            table: "elsa_secrets",
            type: "text",
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "DisplayNameSearchKey",
            table: "elsa_secrets",
            type: "text",
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedName",
            table: "elsa_secrets",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256);

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "elsa_secrets",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "TypeNameLookupKey",
            table: "elsa_secrets",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "StoreNameLookupKey",
            table: "elsa_secrets",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "elsa_secrets",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32,
            oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "ScopeLookupKey",
            table: "elsa_secrets",
            type: "text",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true,
            oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "NameSearchKey",
            table: "elsa_secrets",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "DisplayNameSearchKey",
            table: "elsa_secrets",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedName",
            table: "elsa_secrets",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256,
            oldCollation: "C");

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "elsa_secrets",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256,
            oldCollation: "C");
    }
}
