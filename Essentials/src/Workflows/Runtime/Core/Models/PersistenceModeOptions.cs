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
    /// Maximum number of activities whose work Balanced folds into one commit. Ephemeral has no such limit: it commits
    /// only at suspension, fault and completion, so a long run that does none of these holds its work in memory. The checkpoint that would bring another
    /// activity into a segment that already holds this many forces an intermediate commit, and the next segment starts
    /// empty, so a long loop keeps folding at one commit per window instead of degrading to per-checkpoint persistence.
    /// Bounds both the work a crash repeats and the memory a segment holds. Must be greater than zero. Default is 50.
    /// </summary>
    public int MaxActivitiesPerCommit { get; set; } = 50;

    /// <summary>
    /// Whether an Ephemeral run that completes without ever having been committed writes a completion record. When
    /// off, such a run leaves no workflow, activity, inspection or value rows, and its queued start is removed. A run
    /// that already has a record (it suspended, faulted, or ran an activity that is not safe to repeat) always gets its
    /// completion committed. Has no effect on Durable or Balanced. Default is on.
    /// </summary>
    public bool RecordCompletion { get; set; } = true;

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
