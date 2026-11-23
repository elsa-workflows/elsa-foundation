using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Elsa.Workbench.Readiness;

public static class ShellActivationTelemetry
{
    public const string ActivitySourceName = "Elsa.Workbench.Readiness";
    public const string MeterName = ActivitySourceName;
    public const string ActivityName = "elsa.shell.activation";
    public const string DurationInstrumentName = "elsa.shell.activation.duration";
    public const string PhaseTag = "elsa.activation.phase";
    public const string OutcomeTag = "elsa.activation.outcome";

    public const string OverallPhase = "overall";
    public const string FeatureDiscoveryPhase = "feature_discovery";
    public const string ShellActivationPhase = "shell_activation";
    public const string SuccessOutcome = "success";
    public const string FailedOutcome = "failed";
    public const string CancelledOutcome = "cancelled";

    private static readonly Lazy<ActivitySource> ActivitySource = new(
        () => new(ActivitySourceName),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<Meter> Meter = new(
        () => new(MeterName),
        LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<Histogram<double>> Duration = new(
        () => Meter.Value.CreateHistogram<double>(
            DurationInstrumentName,
            "ms",
            "Default-shell preparation phase duration."),
        LazyThreadSafetyMode.PublicationOnly);

    internal static ActivitySource GetActivitySource() => ActivitySource.Value;

    internal static Histogram<double> GetDuration() => Duration.Value;
}
