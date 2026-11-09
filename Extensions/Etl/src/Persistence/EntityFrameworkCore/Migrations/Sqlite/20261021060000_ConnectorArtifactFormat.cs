using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite;

/// <summary>Adds an explicit format discriminator while retaining every original v1 payload byte.</summary>
[DbContext(typeof(EtlDesignSqliteDbContext))]
[Migration("20261021060000_ConnectorArtifactFormat")]
internal sealed class ConnectorArtifactFormat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("FormatVersion", "elsa_etl_definition_drafts", type: "INTEGER", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>("FormatVersion", "elsa_etl_published_artifacts", type: "INTEGER", nullable: false, defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Removing the discriminator would let old readers mistake connector drafts for file drafts.
        throw new NotSupportedException("Connector artifact storage cannot be downgraded while retaining v2 data. Restore a verified v1 database backup instead.");
    }
}
