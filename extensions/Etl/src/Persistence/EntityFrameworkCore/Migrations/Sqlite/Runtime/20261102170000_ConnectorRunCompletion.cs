using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <summary>Adds the durable completion request of a continuous connector run.</summary>
/// <remarks>Prior writers only created finite runs, which never carry a request, so existing rows keep a null value.</remarks>
[DbContext(typeof(EtlRuntimeSqliteDbContext))]
[Migration("20261102170000_ConnectorRunCompletion")]
internal sealed class ConnectorRunCompletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE "elsa_etl_connector_runs"
            ADD COLUMN "CompletionRequestedAtUtcTicks" INTEGER NULL
            CONSTRAINT "CK_elsa_etl_connector_runs_CompletionRequestedAtUtcTicks"
            CHECK ("CompletionRequestedAtUtcTicks" IS NULL OR "CompletionRequestedAtUtcTicks" > 0);
            """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Connector run completion requests cannot be downgraded safely. Restore a verified database backup instead.");
}
