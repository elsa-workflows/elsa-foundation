namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

/// <summary>
/// Metadata keys used to mark synthetic coalescing checkpoints so the policy and the coalescing commit store can
/// recognise them without a dedicated contract surface.
/// </summary>
public static class RuntimeCoalescingMetadataKeys
{
    /// <summary>
    /// Present on the checkpoint of a folded quiescence-flush commit built by the coalescing drain scope. Signals the
    /// policy to force <c>Immediate</c> persistence and the coalescing commit store to apply the (already-folded)
    /// commit straight to the durable inner store rather than re-buffering it.
    /// </summary>
    public const string CoalescedFlush = "runtime.coalescing.flush";

    /// <summary>Checkpoint name used for the synthetic folded quiescence-flush commit.</summary>
    public const string FlushCheckpointName = "CoalescedSegmentFlush";
}
