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

    private static readonly Lazy<ActivitySource> ActivitySource = new(
        () => new(ActivitySourceName),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<Meter> Meter = new(
        () => new(MeterName),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<Histogram<double>> Duration = new(
        () => Meter.Value.CreateHistogram<double>(DurationInstrumentName, "ms", "Groundwork SQLite initialization duration."),
        LazyThreadSafetyMode.PublicationOnly);

    internal static ActivitySource GetActivitySource() => ActivitySource.Value;

    internal static Histogram<double> GetDuration() => Duration.Value;
}
