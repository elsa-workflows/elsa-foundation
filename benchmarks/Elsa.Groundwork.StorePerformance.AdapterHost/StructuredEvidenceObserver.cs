using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Capture-only observer that preserves the legacy command observer while retaining immutable,
/// callback-produced structured facts. It is attached only by native-plan capture, never by timed
/// benchmark compositions.
/// </summary>
internal sealed class StructuredEvidenceObserver(WritePathRoundTripObserver legacy) : IProviderExecutionObserver, IProviderRoundTripObserver
{
    private readonly List<StructuredExecutionEvidence> observations = [];

    public string Provider => legacy.Provider;

    public string Instrumentation => "groundwork-v2:IProviderExecutionObserver";

    public bool IsExact => legacy.IsExact;

    public ProviderExecutionEvidenceOptions EvidenceOptions => ProviderExecutionEvidenceOptions.ShapeAndPlans;

    public void Observe(ProviderCommandEvent command) => legacy.Observe(command);

    public void ObserveExecution(ProviderExecutionEvidence evidence)
    {
        var mapped = StructuredEvidenceMapper.Map(evidence);
        lock (observations)
            observations.Add(mapped);
    }

    internal IReadOnlyList<StructuredExecutionEvidence> SnapshotEvidence()
    {
        lock (observations)
            return observations.ToArray();
    }

    internal void ClearEvidence()
    {
        lock (observations)
            observations.Clear();
    }

    public long Snapshot() => legacy.Snapshot();
}
