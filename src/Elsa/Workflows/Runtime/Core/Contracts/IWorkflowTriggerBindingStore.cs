using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Durable store for <see cref="WorkflowTriggerBinding"/> entries — the trigger index Elsa 4 was
/// missing (W7, E3-1). Bindings are written at publish time (one per start-trigger activity in a
/// published executable) and read by the stimulus router to start a new workflow instance when a
/// matching stimulus arrives, with no explicit execution id.
/// </summary>
/// <remarks>
/// The index is keyed over PUBLISHED artifacts, not mutable authored definitions: Elsa 4 pins the
/// executable a workflow runs, so a trigger must resolve to the exact published artifact that owns it.
/// Republishing an artifact replaces its bindings via <see cref="DeleteByArtifactAsync"/> followed by
/// <see cref="SaveAsync"/> for each current trigger, so stale triggers from a prior version never fire.
/// </remarks>
public interface IWorkflowTriggerBindingStore
{
    /// <summary>Upserts a single trigger binding, keyed by its <see cref="WorkflowTriggerBinding.TriggerBindingId"/>.</summary>
    ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every binding owned by the given artifact. Returns the number of bindings removed. Used on
    /// republish to clear a prior version's triggers before the current version's triggers are written.
    /// </summary>
    ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-artifact lookup used by the stimulus router: returns every start-trigger binding whose
    /// stimulus identity matches, so a single stimulus can start instances of any workflow that triggers
    /// on it. Keyed by stimulus hash; the store post-filters by stimulus type so a hash shared across two
    /// stimulus types can never cross-match.
    /// </summary>
    ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default);

    /// <summary>Returns every binding owned by the given artifact.</summary>
    ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default);
}
