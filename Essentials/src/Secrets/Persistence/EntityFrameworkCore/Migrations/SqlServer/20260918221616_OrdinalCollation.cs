using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.SqlServer;

/// <inheritdoc />
public partial class OrdinalCollation : Migration
{
    // SQL Server refuses ALTER COLUMN on a column a key or index depends on, so the key and the
    // filtered-list index are dropped and rebuilt around the three columns they cover. EF cannot
    // scaffold that; the columns it can alter in place are left where it put them.
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_elsa_secrets",
            table: "elsa_secrets");

        migrationBuilder.DropIndex(
            name: "IX_elsa_secrets_tenantId_status_normalizedName",
            table: "elsa_secrets");

        migrationBuilder.AlterColumn<string>(
            name: "TypeNameLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            collation: "Latin1_General_100_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldCollation: "Latin1_General_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "StoreNameLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            collation: "Latin1_General_100_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldCollation: "Latin1_General_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "elsa_secrets",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            collation: "Latin1_General_100_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(32)",
            oldMaxLength: 32);

        migrationBuilder.AlterColumn<string>(
            name: "ScopeLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: true,
            collation: "Latin1_General_100_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true,
            oldCollation: "Latin1_General_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "NameSearchKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            collation: "Latin1_General_100_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)");

        migrationBuilder.AlterColumn<string>(
            name: "DisplayNameSearchKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            collation: "Latin1_General_100_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)");

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedName",
            table: "elsa_secrets",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            collation: "Latin1_General_100_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(256)",
            oldMaxLength: 256,
            oldCollation: "Latin1_General_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "elsa_secrets",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            collation: "Latin1_General_100_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(256)",
            oldMaxLength: 256,
            oldCollation: "Latin1_General_BIN2");

        migrationBuilder.AddPrimaryKey(
            name: "PK_elsa_secrets",
            table: "elsa_secrets",
            columns: ["TenantId", "NormalizedName"]);

        migrationBuilder.CreateIndex(
            name: "IX_elsa_secrets_tenantId_status_normalizedName",
            table: "elsa_secrets",
            columns: ["TenantId", "Status", "NormalizedName"]);

    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropPrimaryKey(
            name: "PK_elsa_secrets",
            table: "elsa_secrets");

        migrationBuilder.DropIndex(
            name: "IX_elsa_secrets_tenantId_status_normalizedName",
            table: "elsa_secrets");

        migrationBuilder.AlterColumn<string>(
            name: "TypeNameLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            collation: "Latin1_General_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldCollation: "Latin1_General_100_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "StoreNameLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            collation: "Latin1_General_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldCollation: "Latin1_General_100_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "elsa_secrets",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(32)",
            oldMaxLength: 32,
            oldCollation: "Latin1_General_100_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "ScopeLookupKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: true,
            collation: "Latin1_General_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldNullable: true,
            oldCollation: "Latin1_General_100_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "NameSearchKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldCollation: "Latin1_General_100_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "DisplayNameSearchKey",
            table: "elsa_secrets",
            type: "nvarchar(max)",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldCollation: "Latin1_General_100_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedName",
            table: "elsa_secrets",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            collation: "Latin1_General_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(256)",
            oldMaxLength: 256,
            oldCollation: "Latin1_General_100_BIN2");

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "elsa_secrets",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: false,
            collation: "Latin1_General_BIN2",
            oldClrType: typeof(string),
            oldType: "nvarchar(256)",
            oldMaxLength: 256,
            oldCollation: "Latin1_General_100_BIN2");

        migrationBuilder.AddPrimaryKey(
            name: "PK_elsa_secrets",
            table: "elsa_secrets",
            columns: ["TenantId", "NormalizedName"]);

        migrationBuilder.CreateIndex(
            name: "IX_elsa_secrets_tenantId_status_normalizedName",
            table: "elsa_secrets",
            columns: ["TenantId", "Status", "NormalizedName"]);

    }
}
