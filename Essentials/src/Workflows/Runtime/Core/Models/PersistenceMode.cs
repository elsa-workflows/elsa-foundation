using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// When a run's state is committed to the durable store, and therefore how much work a crash costs. Suspension,
/// bookmark, fault, cancellation and completion checkpoints are committed in every mode, with one exception that a
/// host opts into: see <see cref="Ephemeral"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PersistenceMode>))]
public enum PersistenceMode
{
    /// <summary>
    /// Holds an activity's checkpoints in memory and commits them together, once per activity. After a crash the run
    /// resumes at the last completed activity.
    /// </summary>
    Durable,

    /// <summary>
    /// Folds checkpoints in memory and commits on suspension, fault, completion, before an activity that is not safe to
    /// repeat, at the end of each execution pass and after a bounded amount of work. After a crash the activities since the last commit run again. The default when
    /// neither the workflow nor the host chooses a mode.
    /// </summary>
    Balanced,

    /// <summary>
    /// Commits when the run suspends or faults, before an activity that is not safe to repeat runs, at the end of a pass
    /// that does not complete the run, and once at completion. A host that turns
    /// <see cref="PersistenceModeOptions.RecordCompletion"/> off drops that completion record for a run that was never
    /// committed before, so a run whose activities are all safe to repeat and that completes in one pass leaves no trace;
    /// a run that already has a record always gets its completion committed. After a crash the run starts again from its
    /// last commit, which may be the beginning.
    /// </summary>
    Ephemeral
}
