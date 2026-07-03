namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Options for the burst-coalescing checkpoint persistence policy (E3-6, RT-10). Bounds a coalesced segment so
/// replay cost after a crash and in-memory working-set size stay bounded.
/// </summary>
public sealed class CoalescingRuntimeCheckpointPersistenceOptions
{
    /// <summary>
    /// Maximum number of checkpoints that may be folded into a single coalesced segment before an intermediate flush
    /// is forced and the drain falls back to <c>Immediate</c> persistence for the remainder of the drain. Bounds both
    /// the crash-replay window (a mid-segment crash re-drives at most this many hops) and the working-set memory a
    /// coalesced segment holds. Must be greater than zero. Default is 50.
    /// </summary>
    public int MaxSegmentCheckpoints { get; set; } = 50;
}
