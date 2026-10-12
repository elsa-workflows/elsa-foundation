namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The host's persistence-mode settings. <see cref="Mode"/> applies to every workflow that does not choose its own
/// mode; the other settings shape how Balanced folds checkpoints between commits.
/// </summary>
public sealed class PersistenceModeOptions
{
    /// <summary>The mode a run uses when its workflow does not choose one.</summary>
    public PersistenceMode Mode { get; set; } = PersistenceMode.Balanced;

    /// <summary>
    /// Maximum number of checkpoints that may be folded into a single segment before an intermediate fold-and-flush is
    /// forced. The flush starts a fresh segment (like a durable attempt boundary does), so a replayable hot loop longer
    /// than the cap keeps folding at one durable commit per cap-sized window instead of degrading to per-checkpoint
    /// persistence for the remainder of the drain. Bounds both the crash-replay window (a mid-segment crash re-drives at
    /// most this many hops past the last flush) and the working-set memory a segment holds. Must be greater than zero.
    /// Default is 50.
    /// </summary>
    public int MaxSegmentCheckpoints { get; set; } = 50;

    /// <summary>
    /// When enabled (the default), equivalent durable-value page reads within an eligible folding session may
    /// reuse the session's bounded raw-page memo. Disable to keep issuing the durable page request on each logical
    /// read while retaining the selected mode.
    /// </summary>
    public bool CoalesceDurableValueReads { get; set; } = true;

    /// <summary>
    /// When enabled (the default), activity-execution inspection reads issued while a folding session owns the
    /// workflow execution are served from a per-drain memo of the durable baseline instead of re-reading the durable
    /// store on every hop. The memo caches exactly what the durable store returned (including null misses) and is
    /// invalidated whenever a durable checkpoint commit lands for the session, so every read remains byte-identical to a
    /// fresh durable read: the ownership lease guarantees no other writer can change the rows in between. Disable to
    /// fall back to a per-hop durable read on every inspection-projection build.
    /// </summary>
    public bool CoalesceInspectionReads { get; set; } = true;
}
