using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Elsa.Persistence.Groundwork.Sqlite;

/// <summary>
/// Stable diagnostics vocabulary for Groundwork SQLite initialization.
/// </summary>
public static class SqliteGroundworkTelemetry
{
    public const string ActivitySourceName = "Elsa.Persistence.Groundwork.Sqlite";
    public const string MeterName = ActivitySourceName;
    public const string ActivityName = "elsa.groundwork.initialize";
    public const string DurationInstrumentName = "elsa.groundwork.initialization.duration";
    public const string OutcomeTag = "elsa.groundwork.initialization";

    public const string HistoryHitOutcome = "history_hit";
    public const string MaterializedOutcome = "materialized";
    public const string FailedOutcome = "failed";
    public const string CancelledOutcome = "cancelled";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    internal static readonly Meter Meter = new(MeterName);
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(DurationInstrumentName, "ms", "Groundwork SQLite initialization duration.");
}
