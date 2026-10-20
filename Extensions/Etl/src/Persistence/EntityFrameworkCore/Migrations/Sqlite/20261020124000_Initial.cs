using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite;

[DbContext(typeof(EtlDesignSqliteDbContext))]
[Migration("20261020124000_Initial")]
internal sealed class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "elsa_etl_definition_drafts",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                PersistencePartition = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                DefinitionKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DefinitionId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                LastPublishedVersion = table.Column<int>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                PipelineJson = table.Column<string>(type: "TEXT", nullable: false),
                PipelineHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                UpdatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_elsa_etl_definition_drafts", x => new { x.ScopeKey, x.DefinitionKey }));

        migrationBuilder.CreateTable(
            name: "elsa_etl_published_artifacts",
            columns: table => new
            {
                ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                PersistencePartition = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                DefinitionKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                DefinitionId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                DefinitionVersion = table.Column<int>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                ArtifactJson = table.Column<string>(type: "TEXT", nullable: false),
                ArtifactEnvelopeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PipelineHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                PublishedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_elsa_etl_published_artifacts", x => new { x.ScopeKey, x.DefinitionKey, x.DefinitionVersion }));

        migrationBuilder.CreateTable(
            name: "__ElsaDatabaseIdentity_EtlDesign",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK___ElsaDatabaseIdentity_EtlDesign", x => x.Id));

        migrationBuilder.CreateTable(
            name: "__ElsaSchemaFinalization_EtlDesign",
            columns: table => new
            {
                Family = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                FinalizedVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                FinishHistoryJson = table.Column<string>(type: "TEXT", nullable: false),
                FinishJson = table.Column<string>(type: "TEXT", nullable: true),
                HistoryJson = table.Column<string>(type: "TEXT", nullable: false),
                HoldsJson = table.Column<string>(type: "TEXT", nullable: false),
                IntentJson = table.Column<string>(type: "TEXT", nullable: true),
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK___ElsaSchemaFinalization_EtlDesign", x => x.Family));

        migrationBuilder.CreateIndex("IX_elsa_etl_definition_drafts_SchemaVersion", "elsa_etl_definition_drafts", "SchemaVersion");
        migrationBuilder.CreateIndex("IX_elsa_etl_published_artifacts_SchemaVersion", "elsa_etl_published_artifacts", "SchemaVersion");
        migrationBuilder.CreateIndex("IX___ElsaDatabaseIdentity_EtlDesign_SchemaVersion", "__ElsaDatabaseIdentity_EtlDesign", "SchemaVersion");
        migrationBuilder.CreateIndex("IX___ElsaSchemaFinalization_EtlDesign_SchemaVersion", "__ElsaSchemaFinalization_EtlDesign", "SchemaVersion");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("elsa_etl_definition_drafts");
        migrationBuilder.DropTable("elsa_etl_published_artifacts");
        migrationBuilder.DropTable("__ElsaDatabaseIdentity_EtlDesign");
        migrationBuilder.DropTable("__ElsaSchemaFinalization_EtlDesign");
    }
}
