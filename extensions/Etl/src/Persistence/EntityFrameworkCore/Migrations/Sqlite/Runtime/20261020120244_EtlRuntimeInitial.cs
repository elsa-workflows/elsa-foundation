using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Migrations.Sqlite.Runtime;

/// <inheritdoc />
internal sealed partial class EtlRuntimeInitial : Migration
{
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_EtlRuntime",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_EtlRuntime", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_EtlRuntime",
                columns: table => new
                {
                    Family = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "TEXT", nullable: true),
                    HoldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    HistoryJson = table.Column<string>(type: "TEXT", nullable: false),
                    FinishJson = table.Column<string>(type: "TEXT", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_EtlRuntime", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_etl_runtime_manifest",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    PartitionOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchStartOrdinal = table.Column<long>(type: "INTEGER", nullable: false),
                    Route = table.Column<int>(type: "INTEGER", nullable: false),
                    BatchId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ChunkKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    RecordCount = table.Column<long>(type: "INTEGER", nullable: false),
                    AcceptedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_etl_runtime_manifest", x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.BatchStartOrdinal, x.Route });
                });

            migrationBuilder.CreateTable(
                name: "elsa_etl_runtime_outbox",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    DeliveryId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true),
                    Fence = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Disposition = table.Column<int>(type: "INTEGER", nullable: true),
                    AcknowledgedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_etl_runtime_outbox", x => new { x.ScopeKey, x.RunKey });
                });

            migrationBuilder.CreateTable(
                name: "elsa_etl_runtime_partitions",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    PartitionOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOrdinal = table.Column<long>(type: "INTEGER", nullable: false),
                    EndOrdinalExclusive = table.Column<long>(type: "INTEGER", nullable: false),
                    NextOrdinal = table.Column<long>(type: "INTEGER", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true),
                    Fence = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_etl_runtime_partitions", x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal });
                });

            migrationBuilder.CreateTable(
                name: "elsa_etl_runtime_prepared",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    FormatVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    SourceHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceLength = table.Column<long>(type: "INTEGER", nullable: false),
                    LookupKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    LookupHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LookupLength = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceIndexKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    SourceIndexHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceIndexLength = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceRecordCount = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_etl_runtime_prepared", x => new { x.ScopeKey, x.RunKey });
                });

            migrationBuilder.CreateTable(
                name: "elsa_etl_runtime_reservations",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    PartitionOrdinal = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOrdinal = table.Column<long>(type: "INTEGER", nullable: false),
                    EndOrdinalExclusive = table.Column<long>(type: "INTEGER", nullable: false),
                    BatchId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InputBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    InputHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Settled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_etl_runtime_reservations", x => new { x.ScopeKey, x.RunKey, x.PartitionOrdinal, x.StartOrdinal });
                });

            migrationBuilder.CreateTable(
                name: "elsa_etl_runtime_runs",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true),
                    PersistencePartition = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    InvocationFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InvocationJson = table.Column<string>(type: "TEXT", nullable: false),
                    BindingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PreparationOwner = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: true),
                    PreparationFence = table.Column<long>(type: "INTEGER", nullable: false),
                    PreparationExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    OutstandingRecords = table.Column<long>(type: "INTEGER", nullable: false),
                    OutstandingBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    AcceptedRecords = table.Column<long>(type: "INTEGER", nullable: false),
                    RejectedRecords = table.Column<long>(type: "INTEGER", nullable: false),
                    FilteredRecords = table.Column<long>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_etl_runtime_runs", x => new { x.ScopeKey, x.RunKey });
                });

            migrationBuilder.CreateTable(
                name: "elsa_etl_runtime_terminals",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RunKey = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    ResultId = table.Column<string>(type: "TEXT", maxLength: 1200, nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SettledAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_etl_runtime_terminals", x => new { x.ScopeKey, x.RunKey });
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_EtlRuntime_SchemaVersion",
                table: "__ElsaDatabaseIdentity_EtlRuntime",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_EtlRuntime_SchemaVersion",
                table: "__ElsaSchemaFinalization_EtlRuntime",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_manifest_SchemaVersion",
                table: "elsa_etl_runtime_manifest",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_manifest_ScopeKey_RunKey_BatchId_Route",
                table: "elsa_etl_runtime_manifest",
                columns: new[] { "ScopeKey", "RunKey", "BatchId", "Route" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_outbox_SchemaVersion",
                table: "elsa_etl_runtime_outbox",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_outbox_ScopeKey_AcknowledgedAtUtcTicks_ExpiresAtUtcTicks",
                table: "elsa_etl_runtime_outbox",
                columns: new[] { "ScopeKey", "AcknowledgedAtUtcTicks", "ExpiresAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_partitions_SchemaVersion",
                table: "elsa_etl_runtime_partitions",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_partitions_ScopeKey_RunKey_NextOrdinal",
                table: "elsa_etl_runtime_partitions",
                columns: new[] { "ScopeKey", "RunKey", "NextOrdinal" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_prepared_SchemaVersion",
                table: "elsa_etl_runtime_prepared",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_reservations_SchemaVersion",
                table: "elsa_etl_runtime_reservations",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_reservations_ScopeKey_RunKey_PartitionOrdinal_Settled",
                table: "elsa_etl_runtime_reservations",
                columns: new[] { "ScopeKey", "RunKey", "PartitionOrdinal", "Settled" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_runs_SchemaVersion",
                table: "elsa_etl_runtime_runs",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_runs_ScopeKey_Status_CreatedAtUtcTicks",
                table: "elsa_etl_runtime_runs",
                columns: new[] { "ScopeKey", "Status", "CreatedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_etl_runtime_terminals_SchemaVersion",
                table: "elsa_etl_runtime_terminals",
                column: "SchemaVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_EtlRuntime");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_EtlRuntime");

            migrationBuilder.DropTable(
                name: "elsa_etl_runtime_manifest");

            migrationBuilder.DropTable(
                name: "elsa_etl_runtime_outbox");

            migrationBuilder.DropTable(
                name: "elsa_etl_runtime_partitions");

            migrationBuilder.DropTable(
                name: "elsa_etl_runtime_prepared");

            migrationBuilder.DropTable(
                name: "elsa_etl_runtime_reservations");

            migrationBuilder.DropTable(
                name: "elsa_etl_runtime_runs");

            migrationBuilder.DropTable(
                name: "elsa_etl_runtime_terminals");
        }
}
