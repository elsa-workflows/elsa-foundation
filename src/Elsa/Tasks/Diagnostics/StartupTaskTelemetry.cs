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

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    internal static readonly Meter Meter = new(MeterName);
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(DurationInstrumentName, "ms", "Shell startup-task execution duration.");
}
