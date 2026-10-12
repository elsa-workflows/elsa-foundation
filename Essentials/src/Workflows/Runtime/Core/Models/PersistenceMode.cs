using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// When a run's state is committed to the durable store, and therefore how much work a crash costs. Suspension,
/// bookmark, fault, cancellation and completion checkpoints are committed in every mode.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PersistenceMode>))]
public enum PersistenceMode
{
    /// <summary>
    /// Commits every runtime checkpoint as soon as it is decided. After a crash the run resumes at the last completed
    /// activity.
    /// </summary>
    Durable,

    /// <summary>
    /// Folds checkpoints in memory and commits on suspension, fault, completion, at the end of each execution pass and
    /// after a bounded amount of work. After a crash the activities since the last commit run again.
    /// </summary>
    Balanced
}
