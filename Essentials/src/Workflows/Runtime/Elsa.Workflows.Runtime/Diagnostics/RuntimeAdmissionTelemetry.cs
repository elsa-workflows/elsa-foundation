using System.Diagnostics.Metrics;

namespace Elsa.Workflows.Runtime.Diagnostics;

/// <summary>
/// What the admission controller decided and why, as metrics (#2614). The counters on
/// <see cref="Core.Diagnostics.RuntimeAdmissionDiagnostics"/> say how often the limit moved; these say where it stood,
/// what it was judged against, and which completions moved it, so a shed under load can be explained from a capture
/// rather than inferred from a throughput curve.
/// </summary>
/// <remarks>
/// Synchronous instruments on one static meter: they cost nothing until a listener attaches, and they carry no
/// per-instance callback, so a host that builds more than one controller (tests do) neither leaks nor double-reports.
/// </remarks>
public static class RuntimeAdmissionTelemetry
{
    public const string MeterName = "Elsa.Workflows.Runtime.Admission";
    public const string LimitInstrumentName = "elsa.runtime.admission.limit";
    public const string InFlightInstrumentName = "elsa.runtime.admission.in_flight";
    public const string BaselineInstrumentName = "elsa.runtime.admission.baseline";
    public const string DecisionCountInstrumentName = "elsa.runtime.admission.decisions";
    public const string SampleDurationInstrumentName = "elsa.runtime.admission.sample.duration";
    public const string OutcomeTag = "elsa.runtime.admission.outcome";
    public const string SampleTag = "elsa.runtime.admission.sample";

    public const string AdmittedOutcome = "admitted";
    public const string ShedOutcome = "shed";

    /// <summary>A saturated completion within tolerance of the baseline: the limit rose.</summary>
    public const string IncreasedSample = "increased";

    /// <summary>A saturated completion slower than tolerance allows: the limit fell.</summary>
    public const string DecreasedSample = "decreased";

    /// <summary>
    /// A saturated completion slower than tolerance allows, admitted before the last cut: the cut already answered it, so
    /// the limit held.
    /// </summary>
    public const string SupersededSample = "superseded";

    /// <summary>A completion admitted below half the limit: it moved only the baseline.</summary>
    public const string UnsaturatedSample = "unsaturated";

    /// <summary>The first sampled completion: it set the baseline and nothing else.</summary>
    public const string CalibrationSample = "calibration";

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Gauge<double> Limit = Meter.CreateGauge<double>(
        LimitInstrumentName,
        "{dispatch}",
        "Current admission limit, in dispatch units.");

    internal static readonly Gauge<long> InFlight = Meter.CreateGauge<long>(
        InFlightInstrumentName,
        "{dispatch}",
        "Dispatch units in flight when an admission decision was made.");

    internal static readonly Gauge<double> Baseline = Meter.CreateGauge<double>(
        BaselineInstrumentName,
        "ms",
        "Completion-latency baseline saturated samples are judged against.");

    internal static readonly Counter<long> Decisions = Meter.CreateCounter<long>(
        DecisionCountInstrumentName,
        "{decision}",
        "Admission decisions, by outcome.");

    internal static readonly Histogram<double> SampleDuration = Meter.CreateHistogram<double>(
        SampleDurationInstrumentName,
        "ms",
        "Duration of each sampled admitted command, by what it did to the limit.");
}
