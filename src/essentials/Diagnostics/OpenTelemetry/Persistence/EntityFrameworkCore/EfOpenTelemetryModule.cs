using Elsa.Persistence.EntityFramework;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;

public static class EfOpenTelemetryModule
{
    public const string HistoryModuleName = "ElsaOpenTelemetry";
    public const string ResourceTable = "elsa_otel_resources";
    public const string TraceTable = "elsa_otel_traces";
    public const string SpanTable = "elsa_otel_spans";
    public const string InstrumentTable = "elsa_otel_instruments";
    public const string MetricPointTable = "elsa_otel_metric_points";
    public const string LogTable = "elsa_otel_logs";
    public const string LedgerTable = "elsa_otel_capture_ledger";
    public const string SummaryTable = "elsa_otel_trace_summaries";
    public const string MembershipTable = "elsa_otel_trace_summary_memberships";

    /// <summary>The persisted-schema version every OpenTelemetry row is stamped with, and checked against when read.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>The schema family this module's rows belong to, checked against when a row is read.</summary>
    public const string SchemaFamily = "OpenTelemetry";
    /// <summary>The family's one chain, from this assembly's declaration: what every reader of the family checks and
    /// upcasts through (spec 180, FR-010).</summary>
    public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(EfOpenTelemetryModule).Assembly, SchemaFamily);
    public const string DefaultConnectionName = "ElsaOpenTelemetry";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-opentelemetry.db";

    public static string HistoryTableName => EfMigrationsHistory.TableName(HistoryModuleName);
}
