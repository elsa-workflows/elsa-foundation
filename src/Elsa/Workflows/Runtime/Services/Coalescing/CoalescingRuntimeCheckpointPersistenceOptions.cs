namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Options for the burst-coalescing checkpoint persistence policy (E3-6, RT-10). Bounds a coalesced segment so
/// replay cost after a crash and in-memory working-set size stay bounded.
/// </summary>
public sealed class CoalescingRuntimeCheckpointPersistenceOptions
{
    /// <summary>
    /// Maximum number of checkpoints that may be folded into a single coalesced segment before an intermediate
    /// fold-and-flush is forced. The flush starts a fresh coalesced segment (like a durable attempt boundary does), so
    /// a replayable hot loop longer than the cap keeps coalescing at one durable commit per cap-sized window instead
    /// of degrading to per-checkpoint persistence for the remainder of the drain. Bounds both the crash-replay window
    /// (a mid-segment crash re-drives at most this many hops past the last flush) and the working-set memory a
    /// coalesced segment holds. Must be greater than zero. Default is 50.
    /// </summary>
    public int MaxSegmentCheckpoints { get; set; } = 50;

    /// <summary>
    /// When enabled (the default), <see cref="Contracts.IActivityExecutionInspectionStore.FindAsync"/> reads issued
    /// while a coalescing session owns the workflow execution are served from a per-drain memo of the durable
    /// baseline instead of re-reading the durable store on every hop. The memo caches exactly what the durable store
    /// returned (including null misses) and is invalidated whenever a durable checkpoint commit lands for the session,
    /// so every read remains byte-identical to a fresh durable read — the ownership lease guarantees no other writer
    /// can change the rows in between. Disable to fall back to a per-hop durable read on every inspection-projection
    /// build (the behavioral control).
    /// </summary>
    public bool CoalesceInspectionReads { get; set; } = true;
}
