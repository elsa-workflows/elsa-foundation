using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.MySql;

/// <inheritdoc />
public partial class OrdinalCollation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase(
            oldCollation: "utf8mb4_0900_bin")
            .Annotation("MySQL:Charset", "utf8mb4")
            .OldAnnotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.AlterTable(
            name: "elsa_secrets")
            .Annotation("MySQL:Charset", "utf8mb4")
            .OldAnnotation("MySQL:Charset", "utf8mb4")
            .OldAnnotation("Relational:Collation", "utf8mb4_0900_bin");

        migrationBuilder.AlterColumn<string>(
            name: "TypeNameLookupKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: false,
            collation: "utf8mb4_0900_bin",
            oldClrType: typeof(string),
            oldType: "longtext");

        migrationBuilder.AlterColumn<string>(
            name: "StoreNameLookupKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: false,
            collation: "utf8mb4_0900_bin",
            oldClrType: typeof(string),
            oldType: "longtext");

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "elsa_secrets",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            collation: "utf8mb4_0900_bin",
            oldClrType: typeof(string),
            oldType: "varchar(32)",
            oldMaxLength: 32);

        migrationBuilder.AlterColumn<string>(
            name: "ScopeLookupKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: true,
            collation: "utf8mb4_0900_bin",
            oldClrType: typeof(string),
            oldType: "longtext",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "NameSearchKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: false,
            collation: "utf8mb4_0900_bin",
            oldClrType: typeof(string),
            oldType: "longtext");

        migrationBuilder.AlterColumn<string>(
            name: "DisplayNameSearchKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: false,
            collation: "utf8mb4_0900_bin",
            oldClrType: typeof(string),
            oldType: "longtext");

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedName",
            table: "elsa_secrets",
            type: "varchar(256)",
            maxLength: 256,
            nullable: false,
            collation: "utf8mb4_0900_bin",
            oldClrType: typeof(string),
            oldType: "varchar(256)",
            oldMaxLength: 256);

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "elsa_secrets",
            type: "varchar(256)",
            maxLength: 256,
            nullable: false,
            collation: "utf8mb4_0900_bin",
            oldClrType: typeof(string),
            oldType: "varchar(256)",
            oldMaxLength: 256);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase(
            collation: "utf8mb4_0900_bin")
            .Annotation("MySQL:Charset", "utf8mb4")
            .OldAnnotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.AlterTable(
            name: "elsa_secrets")
            .Annotation("MySQL:Charset", "utf8mb4")
            .Annotation("Relational:Collation", "utf8mb4_0900_bin")
            .OldAnnotation("MySQL:Charset", "utf8mb4");

        migrationBuilder.AlterColumn<string>(
            name: "TypeNameLookupKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "longtext",
            oldCollation: "utf8mb4_0900_bin");

        migrationBuilder.AlterColumn<string>(
            name: "StoreNameLookupKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "longtext",
            oldCollation: "utf8mb4_0900_bin");

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "elsa_secrets",
            type: "varchar(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "varchar(32)",
            oldMaxLength: 32,
            oldCollation: "utf8mb4_0900_bin");

        migrationBuilder.AlterColumn<string>(
            name: "ScopeLookupKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "longtext",
            oldNullable: true,
            oldCollation: "utf8mb4_0900_bin");

        migrationBuilder.AlterColumn<string>(
            name: "NameSearchKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "longtext",
            oldCollation: "utf8mb4_0900_bin");

        migrationBuilder.AlterColumn<string>(
            name: "DisplayNameSearchKey",
            table: "elsa_secrets",
            type: "longtext",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "longtext",
            oldCollation: "utf8mb4_0900_bin");

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedName",
            table: "elsa_secrets",
            type: "varchar(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "varchar(256)",
            oldMaxLength: 256,
            oldCollation: "utf8mb4_0900_bin");

        migrationBuilder.AlterColumn<string>(
            name: "TenantId",
            table: "elsa_secrets",
            type: "varchar(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "varchar(256)",
            oldMaxLength: 256,
            oldCollation: "utf8mb4_0900_bin");
    }
}
