namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Options for cross-drain group commit on the shared Groundwork durable writer (spec 115). When enabled, concurrent
/// checkpoint commits that are waiting on the single durable writer share one atomic unit-of-work / one fsync instead
/// of serializing one transaction per run. Presence of a registered <see cref="RuntimeGroupCommitCoordinator"/> is what
/// activates the group-commit path in <see cref="GroundworkRuntimeCheckpointWriter"/>; a lone committer is never
/// batched and never waits (see <see cref="RuntimeGroupCommitCoordinator"/>).
/// </summary>
public sealed class RuntimeGroupCommitOptions
{
    /// <summary>
    /// Maximum number of concurrent commits folded into a single shared durable transaction before the remainder is
    /// deferred to the next batch. Bounds the size of one atomic unit-of-work (and therefore the blast radius of a
    /// single member's optimistic-concurrency conflict, which rolls the shared transaction back and re-drives every
    /// member individually). Must be greater than one — a max of one would disable batching. Default is 64.
    /// </summary>
    public int MaxBatchSize { get; set; } = 64;
}
