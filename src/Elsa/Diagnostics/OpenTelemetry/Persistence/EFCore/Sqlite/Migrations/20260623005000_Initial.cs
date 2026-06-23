using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(OpenTelemetryDbContext))]
    [Migration("20260623005000_Initial")]
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Elsa");

            migrationBuilder.CreateTable(
                name: "MetricInstruments",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    AttributesJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricInstruments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetricPoints",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PointId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InstrumentId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    InstrumentName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: true),
                    Sum = table.Column<double>(type: "REAL", nullable: true),
                    Count = table.Column<long>(type: "INTEGER", nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OtlpLogRecords",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LogRecordId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Timestamp = table.Column<string>(type: "TEXT", nullable: false),
                    SeverityText = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SeverityNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Body = table.Column<string>(type: "TEXT", maxLength: -1, nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtlpLogRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryResources",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ServiceInstanceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    TelemetrySdkLanguage = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    LastSeen = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryResources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetrySpans",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpanRecordId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SpanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ParentSpanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StartTime = table.Column<string>(type: "TEXT", nullable: false),
                    EndTime = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusDescription = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    AttributesJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    EventsJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    LinksJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetrySpans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryTraces",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RootSpanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    StartTime = table.Column<string>(type: "TEXT", nullable: false),
                    EndTime = table.Column<string>(type: "TEXT", nullable: false),
                    DurationTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceIdsJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    WorkflowInstanceIdsJson = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    SpanCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryTraces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersistedMetricInstrument_Name",
                schema: "Elsa",
                table: "MetricInstruments",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedMetricInstrument_ResourceId",
                schema: "Elsa",
                table: "MetricInstruments",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedMetricPoint_InstrumentId",
                schema: "Elsa",
                table: "MetricPoints",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedMetricPoint_ResourceId",
                schema: "Elsa",
                table: "MetricPoints",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedMetricPoint_Timestamp",
                schema: "Elsa",
                table: "MetricPoints",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedOtlpLogRecord_ResourceId",
                schema: "Elsa",
                table: "OtlpLogRecords",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedOtlpLogRecord_Timestamp",
                schema: "Elsa",
                table: "OtlpLogRecords",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedOtlpLogRecord_TraceId",
                schema: "Elsa",
                table: "OtlpLogRecords",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetryResource_LastSeen",
                schema: "Elsa",
                table: "TelemetryResources",
                column: "LastSeen");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetryResource_ServiceName",
                schema: "Elsa",
                table: "TelemetryResources",
                column: "ServiceName");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetryResource_Status",
                schema: "Elsa",
                table: "TelemetryResources",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetrySpan_ResourceId",
                schema: "Elsa",
                table: "TelemetrySpans",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetrySpan_StartTime",
                schema: "Elsa",
                table: "TelemetrySpans",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetrySpan_TraceId",
                schema: "Elsa",
                table: "TelemetrySpans",
                column: "TraceId");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetryTrace_StartTime",
                schema: "Elsa",
                table: "TelemetryTraces",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetryTrace_Status",
                schema: "Elsa",
                table: "TelemetryTraces",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedTelemetryTrace_TraceId",
                schema: "Elsa",
                table: "TelemetryTraces",
                column: "TraceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MetricInstruments", schema: "Elsa");
            migrationBuilder.DropTable(name: "MetricPoints", schema: "Elsa");
            migrationBuilder.DropTable(name: "OtlpLogRecords", schema: "Elsa");
            migrationBuilder.DropTable(name: "TelemetryResources", schema: "Elsa");
            migrationBuilder.DropTable(name: "TelemetrySpans", schema: "Elsa");
            migrationBuilder.DropTable(name: "TelemetryTraces", schema: "Elsa");
        }
    }
}
