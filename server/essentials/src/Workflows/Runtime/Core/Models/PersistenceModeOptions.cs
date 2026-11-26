namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The host's persistence-mode settings. <see cref="Mode"/> applies to every workflow that does not choose its own
/// mode. <see cref="MaxActivitiesPerCommit"/> is the cap Balanced folds to, <see cref="RecordCompletion"/> is read by
/// Ephemeral only, and <see cref="CoalesceDurableValueReads"/> applies to the session of a run in any mode.
/// </summary>
public sealed class PersistenceModeOptions
{
    /// <summary>The mode a run uses when its workflow does not choose one.</summary>
    public PersistenceMode Mode { get; set; } = PersistenceMode.Balanced;

    /// <summary>
    /// Maximum number of activities whose work Balanced folds into one commit. Durable commits once per activity
    /// whatever this is. Ephemeral has no such limit: it folds until the run suspends, faults or is cancelled, until an
    /// incident is recorded or resolved or the run is altered, until it dispatches a child workflow, until an activity
    /// that is not ReplaySafe is about to run, or until the pass ends, so a long pass that meets none of these holds
    /// its work in memory. The checkpoint that would bring another activity into a segment that already holds this many
    /// forces an intermediate commit, and the next segment starts empty, so a long loop keeps folding at one commit per
    /// window instead of degrading to per-checkpoint persistence. Bounds both the work a crash repeats and the memory a
    /// segment holds. Must be greater than zero. Default is 50.
    /// </summary>
    public int MaxActivitiesPerCommit { get; set; } = 50;

    /// <summary>
    /// Whether an Ephemeral run that completes without ever having been committed writes a completion record. When off,
    /// such a run leaves no workflow, activity, inspection or value rows, and its queued start is removed. A run that
    /// already has a record (for example it suspended, faulted, recorded an incident, dispatched a child or ran an
    /// activity that is not safe to repeat) always gets its completion committed. Has no effect on Durable or Balanced.
    /// Default is on.
    /// </summary>
    public bool RecordCompletion { get; set; } = true;

    /// <summary>
    /// When enabled (the default), equivalent durable-value page reads within an eligible session may reuse the
    /// session's bounded raw-page memo. Disable to keep issuing the durable page request on each logical
    /// read while retaining the selected mode.
    /// </summary>
    public bool CoalesceDurableValueReads { get; set; } = true;
}
