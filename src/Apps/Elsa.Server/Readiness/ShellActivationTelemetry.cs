using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Elsa.Server.Readiness;

public static class ShellActivationTelemetry
{
    public const string ActivitySourceName = "Elsa.Server.Readiness";
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

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    internal static readonly Meter Meter = new(MeterName);
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        DurationInstrumentName,
        "ms",
        "Default-shell preparation phase duration.");
}
