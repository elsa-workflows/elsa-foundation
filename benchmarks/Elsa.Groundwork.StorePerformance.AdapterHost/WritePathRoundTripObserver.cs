using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Bridges Groundwork v2's <see cref="IWritePathObserver"/> onto the harness's
/// <see cref="IProviderRoundTripObserver"/>.
///
/// This is the only exact provider-native command counter Groundwork v2 exposes. The provider sessions
/// raise one <see cref="WritePathEvent"/> per issued provider command — including one per batched
/// statement on the unit-of-work path, where the batch takes its observer from the first staged write's
/// <c>WriteOptions</c> (see <c>PostgreSqlStorageSession.ApplyUpsertBatch</c>) — so the count is provider
/// round trips rather than adapter method calls. That is what <c>ProcessMeasurement</c> demands: it
/// refuses a measured process whose observer is absent or not exact.
///
/// Probe commands are counted like any other: they are real round trips, and excluding them would
/// understate the provider work a named public operation costs.
///
/// The write path is the whole of the seam. <c>IStorageSession.Query</c> takes no observer and Groundwork
/// v2 declares no read-path observer interface, so read-dominated workloads cannot yet be measured
/// exactly on v2 — see this project's README.
/// </summary>
internal sealed class WritePathRoundTripObserver(string provider) : IWritePathObserver, IProviderRoundTripObserver
{
    private long count;

    public string Provider { get; } = provider;

    /// <summary>Names the seam in the artifact so a reader can tell what the number was counted from.</summary>
    public string Instrumentation => "groundwork-v2:IWritePathObserver";

    public bool IsExact => true;

    public void Observe(WritePathEvent command) => Interlocked.Increment(ref count);

    public long Snapshot() => Interlocked.Read(ref count);
}
