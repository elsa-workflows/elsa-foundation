using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Migrations.OpenTelemetry.MySql
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_capture_ledger",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    BatchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    IssuedAtTicks = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAtOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_capture_ledger", x => new { x.ScopeKey, x.BatchId });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_instruments",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    IdOrderKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    IdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3584, nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    ResourceIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3584, nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    NameSearchKey = table.Column<string>(type: "longtext", unicode: false, nullable: false),
                    LastSeenTicks = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_instruments", x => new { x.ScopeKey, x.IdOrderKey });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_logs",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    ResourceIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3584, nullable: false),
                    ServiceName = table.Column<string>(type: "longtext", nullable: true),
                    ServiceNameKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    TraceId = table.Column<string>(type: "longtext", nullable: true),
                    TraceIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 1792, nullable: true),
                    SpanId = table.Column<string>(type: "longtext", nullable: true),
                    SpanIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 1792, nullable: true),
                    SeverityText = table.Column<string>(type: "longtext", nullable: false),
                    SeveritySearchKey = table.Column<string>(type: "longtext", unicode: false, nullable: false),
                    SeverityNumber = table.Column<int>(type: "int", nullable: true),
                    Body = table.Column<string>(type: "longtext", nullable: false),
                    BodySearchKey = table.Column<string>(type: "longtext", unicode: false, nullable: false),
                    TimestampTicks = table.Column<long>(type: "bigint", nullable: false),
                    TimestampOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    IdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 896, nullable: false),
                    IdOrderKey = table.Column<string>(type: "varchar(255)", nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_logs", x => new { x.ScopeKey, x.Sequence });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_metric_points",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    InstrumentId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    InstrumentIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 1792, nullable: false),
                    InstrumentName = table.Column<string>(type: "longtext", nullable: false),
                    InstrumentNameSearchKey = table.Column<string>(type: "longtext", unicode: false, nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    ResourceIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3584, nullable: false),
                    ServiceName = table.Column<string>(type: "longtext", nullable: true),
                    ServiceNameKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    TimestampTicks = table.Column<long>(type: "bigint", nullable: false),
                    TimestampOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    IdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 896, nullable: false),
                    IdOrderKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_metric_points", x => new { x.ScopeKey, x.Sequence });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_resources",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    IdOrderKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    IdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3584, nullable: false),
                    ServiceName = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    ServiceNameSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3584, nullable: false),
                    ServiceNameKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LastSeenTicks = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_resources", x => new { x.ScopeKey, x.IdOrderKey });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_spans",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    TraceId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    TraceIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 1792, nullable: false),
                    TraceKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SpanId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    SpanIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 896, nullable: false),
                    SpanIdOrderKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    ResourceIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3584, nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    NameSearchKey = table.Column<string>(type: "longtext", unicode: false, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartTimeTicks = table.Column<long>(type: "bigint", nullable: false),
                    StartTimeOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    EndTimeTicks = table.Column<long>(type: "bigint", nullable: false),
                    EndTimeOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    IdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 896, nullable: false),
                    IdOrderKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_spans", x => new { x.ScopeKey, x.Sequence });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_trace_summaries",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TraceKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TraceId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    TraceIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 1792, nullable: false),
                    RootSpanId = table.Column<string>(type: "longtext", nullable: true),
                    Name = table.Column<string>(type: "varchar(571)", maxLength: 571, nullable: true),
                    NameSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3997, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartTimeTicks = table.Column<long>(type: "bigint", nullable: false),
                    StartTimeOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    EndTimeTicks = table.Column<long>(type: "bigint", nullable: false),
                    EndTimeOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    SpanCount = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    ServiceMembershipJson = table.Column<string>(type: "longtext", nullable: false),
                    WorkflowMembershipJson = table.Column<string>(type: "longtext", nullable: false),
                    Version = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_trace_summaries", x => new { x.ScopeKey, x.TraceKey });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_trace_summary_memberships",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TraceKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ValueKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    ValueSearchKey = table.Column<string>(type: "varchar(3584)", maxLength: 3584, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_trace_summary_memberships", x => new { x.ScopeKey, x.TraceKey, x.Kind, x.ValueKey });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_otel_traces",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    TraceId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    TraceIdSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 1792, nullable: false),
                    TraceKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    RootSpanId = table.Column<string>(type: "longtext", nullable: true),
                    Name = table.Column<string>(type: "varchar(571)", maxLength: 571, nullable: true),
                    NameSearchKey = table.Column<string>(type: "text", unicode: false, maxLength: 3997, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartTimeTicks = table.Column<long>(type: "bigint", nullable: false),
                    StartTimeOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    EndTimeTicks = table.Column<long>(type: "bigint", nullable: false),
                    EndTimeOffsetMinutes = table.Column<short>(type: "smallint", nullable: false),
                    SpanCount = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    IdSearchKey = table.Column<string>(type: "longtext", unicode: false, nullable: false),
                    IdOrderKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_traces", x => new { x.ScopeKey, x.Sequence });
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_capture_ledger_ScopeKey_IssuedAtTicks",
                table: "elsa_otel_capture_ledger",
                columns: new[] { "ScopeKey", "IssuedAtTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_instruments_ScopeKey_IdOrderKey",
                table: "elsa_otel_instruments",
                columns: new[] { "ScopeKey", "IdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_logs_ScopeKey_TimestampTicks_IdOrderKey",
                table: "elsa_otel_logs",
                columns: new[] { "ScopeKey", "TimestampTicks", "IdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_metric_points_ScopeKey_TimestampTicks_IdOrderKey",
                table: "elsa_otel_metric_points",
                columns: new[] { "ScopeKey", "TimestampTicks", "IdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_resources_ScopeKey_LastSeenTicks_IdOrderKey",
                table: "elsa_otel_resources",
                columns: new[] { "ScopeKey", "LastSeenTicks", "IdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_resources_ScopeKey_ServiceNameKey_LastSeenTicks",
                table: "elsa_otel_resources",
                columns: new[] { "ScopeKey", "ServiceNameKey", "LastSeenTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_spans_ScopeKey_TraceKey_StartTimeTicks_SpanIdOrder~",
                table: "elsa_otel_spans",
                columns: new[] { "ScopeKey", "TraceKey", "StartTimeTicks", "SpanIdOrderKey", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_trace_summaries_ScopeKey_StartTimeTicks_TraceKey",
                table: "elsa_otel_trace_summaries",
                columns: new[] { "ScopeKey", "StartTimeTicks", "TraceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_trace_summary_memberships_ScopeKey_Kind_ValueKey_T~",
                table: "elsa_otel_trace_summary_memberships",
                columns: new[] { "ScopeKey", "Kind", "ValueKey", "TraceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_traces_ScopeKey_StartTimeTicks_TraceKey",
                table: "elsa_otel_traces",
                columns: new[] { "ScopeKey", "StartTimeTicks", "TraceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_traces_ScopeKey_TraceKey_Sequence",
                table: "elsa_otel_traces",
                columns: new[] { "ScopeKey", "TraceKey", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_otel_capture_ledger");

            migrationBuilder.DropTable(
                name: "elsa_otel_instruments");

            migrationBuilder.DropTable(
                name: "elsa_otel_logs");

            migrationBuilder.DropTable(
                name: "elsa_otel_metric_points");

            migrationBuilder.DropTable(
                name: "elsa_otel_resources");

            migrationBuilder.DropTable(
                name: "elsa_otel_spans");

            migrationBuilder.DropTable(
                name: "elsa_otel_trace_summaries");

            migrationBuilder.DropTable(
                name: "elsa_otel_trace_summary_memberships");

            migrationBuilder.DropTable(
                name: "elsa_otel_traces");
        }
    }
}
