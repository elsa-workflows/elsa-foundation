namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;

public static class EfOpenTelemetryModule
{
    public const string ModuleName = "ElsaOpenTelemetry";
    public const string HistoryTableName = "__EFMigrationsHistory_ElsaOpenTelemetry";
    public const string ResourceTable = "elsa_otel_resources";
    public const string TraceTable = "elsa_otel_traces";
    public const string SpanTable = "elsa_otel_spans";
    public const string InstrumentTable = "elsa_otel_instruments";
    public const string MetricPointTable = "elsa_otel_metric_points";
    public const string LogTable = "elsa_otel_logs";
    public const string LedgerTable = "elsa_otel_capture_ledger";
    public const string SummaryTable = "elsa_otel_trace_summaries";
    public const string MembershipTable = "elsa_otel_trace_summary_memberships";
    public const string DefaultConnectionName = "ElsaOpenTelemetry";
    public const string DefaultSqliteConnectionString = "Data Source=elsa-opentelemetry.db";
}
