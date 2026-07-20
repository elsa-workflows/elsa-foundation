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
}
