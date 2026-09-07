using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Groundwork.Kernel;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Bridges Groundwork v2's <see cref="IProviderCommandObserver"/> onto the harness's
/// <see cref="IProviderRoundTripObserver"/>.
///
/// The session-scoped observer raises one <see cref="ProviderCommandEvent"/> per provider command a
/// session or unit of work issues — reads, writes, probes and retention alike — so the count is provider
/// round trips rather than adapter method calls, which is what <c>ProcessMeasurement</c> demands: it
/// refuses a measured process whose observer is absent or not exact. The event's <c>Kind</c> is ignored
/// deliberately: the harness's contract is total round trips, and both kinds are round trips.
///
/// Two count caveats to carry with any published number. Counts made against Groundwork 0.2.x are not
/// comparable to these: the write-path observer this replaces could not see reads, so the same workload
/// now counts higher — that is added visibility, not added cost. And relational append's
/// idempotency-ledger commands (about five per append) are not yet observed upstream, so append-heavy
/// workloads undercount by that overhead; declared on valence-works/groundwork-v2#63.
///
/// Schema work never reaches a session observer — <c>ISchemaCoordinator</c> hangs off the connection —
/// so admission-time DDL is structurally excluded from these counts.
/// </summary>
internal sealed class WritePathRoundTripObserver(
    string provider,
    bool captureCommands = false,
    Action<ProviderCommandEvent>? commandStarting = null) : IProviderCommandObserver, IProviderRoundTripObserver
{
    private long count;
    private readonly List<ProviderCommandEvent> commands = [];

    public string Provider { get; } = provider;

    /// <summary>Names the seam in the artifact so a reader can tell what the number was counted from.</summary>
    public string Instrumentation => "groundwork-v2:IProviderCommandObserver";

    public bool IsExact => true;

    public void Observe(ProviderCommandEvent command)
    {
        commandStarting?.Invoke(command);
        Interlocked.Increment(ref count);
        if (captureCommands)
            lock (commands)
                commands.Add(command);
    }

    internal IReadOnlyList<ProviderCommandEvent> Commands
    {
        get
        {
            lock (commands)
                return commands.ToArray();
        }
    }

    internal void ClearCommands()
    {
        lock (commands)
            commands.Clear();
    }

    public long Snapshot() => Interlocked.Read(ref count);
}
