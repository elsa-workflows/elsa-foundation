using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Elsa.Tasks.Diagnostics;

/// <summary>
/// Stable diagnostics vocabulary for shell startup tasks.
/// </summary>
public static class StartupTaskTelemetry
{
    public const string ActivitySourceName = "Elsa.Tasks.Startup";
    public const string MeterName = ActivitySourceName;
    public const string ActivityName = "elsa.startup_task";
    public const string DurationInstrumentName = "elsa.startup_task.duration";
    public const string TaskTypeTag = "elsa.task.type";
    public const string OutcomeTag = "elsa.task.outcome";

    public const string SuccessOutcome = "success";
    public const string FailedOutcome = "failed";
    public const string CancelledOutcome = "cancelled";
    public const string SkippedOutcome = "skipped";

    private static readonly Lazy<ActivitySource> ActivitySource = new(
        () => new(ActivitySourceName),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<Meter> Meter = new(
        () => new(MeterName),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<Histogram<double>> Duration = new(
        () => Meter.Value.CreateHistogram<double>(DurationInstrumentName, "ms", "Shell startup-task execution duration."),
        LazyThreadSafetyMode.PublicationOnly);

    internal static ActivitySource GetActivitySource() => ActivitySource.Value;

    internal static Histogram<double> GetDuration() => Duration.Value;
}
