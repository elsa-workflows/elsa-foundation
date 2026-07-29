using System.Diagnostics.Metrics;

namespace Elsa.Workflows.Runtime.Core.Diagnostics;

/// <summary>Stable, bounded diagnostics vocabulary for workflow executable caching.</summary>
public static class WorkflowExecutableCacheTelemetry
{
    public const string MeterName = "Elsa.Workflows.Runtime.ExecutableCache";
    public const string RequestCountInstrumentName = "elsa.workflow_executable_cache.requests";
    public const string EvictionCountInstrumentName = "elsa.workflow_executable_cache.evictions";
    public const string ProviderLoadDurationInstrumentName = "elsa.workflow_executable_cache.provider_load.duration";
    public const string ResultTag = "elsa.workflow_executable.cache.result";
    public const string ReasonTag = "elsa.workflow_executable.cache.reason";
    public const string OutcomeTag = "elsa.workflow_executable.provider_load.outcome";

    public const string HitResult = "hit";
    public const string MissResult = "miss";
    public const string CapacityReason = "capacity";
    public const string SaveReason = "save";
    public const string DeleteReason = "delete";
    public const string FoundOutcome = "found";
    public const string NotFoundOutcome = "not_found";
    public const string FailedOutcome = "failed";
    public const string CancelledOutcome = "cancelled";

    internal static readonly Meter Meter = new(MeterName);
    internal static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        RequestCountInstrumentName,
        description: "Workflow executable cache lookup count.");
    internal static readonly Counter<long> Evictions = Meter.CreateCounter<long>(
        EvictionCountInstrumentName,
        description: "Workflow executable cache eviction count.");
    internal static readonly Histogram<double> ProviderLoadDuration = Meter.CreateHistogram<double>(
        ProviderLoadDurationInstrumentName,
        "ms",
        "Durable workflow executable provider-load duration.");
}
