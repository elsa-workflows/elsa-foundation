using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Migrations.OpenTelemetry.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_otel_capture_ledger",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IssuedAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    IssuedAtOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_capture_ledger", x => new { x.ScopeKey, x.BatchId });
                });

            migrationBuilder.CreateTable(
                name: "elsa_otel_instruments",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdOrderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3584, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ResourceIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3584, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NameSearchKey = table.Column<string>(type: "TEXT", unicode: false, nullable: false),
                    LastSeenTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_instruments", x => new { x.ScopeKey, x.IdOrderKey });
                });

            migrationBuilder.CreateTable(
                name: "elsa_otel_logs",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ResourceIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3584, nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceNameKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TraceId = table.Column<string>(type: "TEXT", nullable: true),
                    TraceIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 1792, nullable: true),
                    SpanId = table.Column<string>(type: "TEXT", nullable: true),
                    SpanIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 1792, nullable: true),
                    SeverityText = table.Column<string>(type: "TEXT", nullable: false),
                    SeveritySearchKey = table.Column<string>(type: "TEXT", unicode: false, nullable: false),
                    SeverityNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    BodySearchKey = table.Column<string>(type: "TEXT", unicode: false, nullable: false),
                    TimestampTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 896, nullable: false),
                    IdOrderKey = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_logs", x => new { x.ScopeKey, x.Sequence });
                });

            migrationBuilder.CreateTable(
                name: "elsa_otel_metric_points",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    InstrumentId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    InstrumentIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 1792, nullable: false),
                    InstrumentName = table.Column<string>(type: "TEXT", nullable: false),
                    InstrumentNameSearchKey = table.Column<string>(type: "TEXT", unicode: false, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ResourceIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3584, nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceNameKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TimestampTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 896, nullable: false),
                    IdOrderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_metric_points", x => new { x.ScopeKey, x.Sequence });
                });

            migrationBuilder.CreateTable(
                name: "elsa_otel_resources",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdOrderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3584, nullable: false),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ServiceNameSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3584, nullable: false),
                    ServiceNameKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSeenTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_resources", x => new { x.ScopeKey, x.IdOrderKey });
                });

            migrationBuilder.CreateTable(
                name: "elsa_otel_spans",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TraceIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 1792, nullable: false),
                    TraceKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SpanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SpanIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 896, nullable: false),
                    SpanIdOrderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ResourceIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3584, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NameSearchKey = table.Column<string>(type: "TEXT", unicode: false, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTimeTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    StartTimeOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    EndTimeTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    EndTimeOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 896, nullable: false),
                    IdOrderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_spans", x => new { x.ScopeKey, x.Sequence });
                });

            migrationBuilder.CreateTable(
                name: "elsa_otel_trace_summaries",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TraceKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TraceIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 1792, nullable: false),
                    RootSpanId = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 571, nullable: true),
                    NameSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3997, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTimeTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    StartTimeOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    EndTimeTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    EndTimeOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    SpanCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceMembershipJson = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowMembershipJson = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_trace_summaries", x => new { x.ScopeKey, x.TraceKey });
                });

            migrationBuilder.CreateTable(
                name: "elsa_otel_trace_summary_memberships",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TraceKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ValueKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ValueSearchKey = table.Column<string>(type: "TEXT", maxLength: 3584, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_trace_summary_memberships", x => new { x.ScopeKey, x.TraceKey, x.Kind, x.ValueKey });
                });

            migrationBuilder.CreateTable(
                name: "elsa_otel_traces",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TraceIdSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 1792, nullable: false),
                    TraceKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RootSpanId = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 571, nullable: true),
                    NameSearchKey = table.Column<string>(type: "TEXT", unicode: false, maxLength: 3997, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTimeTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    StartTimeOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    EndTimeTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    EndTimeOffsetMinutes = table.Column<short>(type: "INTEGER", nullable: false),
                    SpanCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IdSearchKey = table.Column<string>(type: "TEXT", unicode: false, nullable: false),
                    IdOrderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_otel_traces", x => new { x.ScopeKey, x.Sequence });
                });

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
                name: "IX_elsa_otel_spans_ScopeKey_TraceKey_StartTimeTicks_SpanIdOrderKey_Sequence",
                table: "elsa_otel_spans",
                columns: new[] { "ScopeKey", "TraceKey", "StartTimeTicks", "SpanIdOrderKey", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_trace_summaries_ScopeKey_StartTimeTicks_TraceKey",
                table: "elsa_otel_trace_summaries",
                columns: new[] { "ScopeKey", "StartTimeTicks", "TraceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_otel_trace_summary_memberships_ScopeKey_Kind_ValueKey_TraceKey",
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
