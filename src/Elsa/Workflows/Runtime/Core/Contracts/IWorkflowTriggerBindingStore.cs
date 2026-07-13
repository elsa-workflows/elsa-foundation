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

    /// <summary>Atomically replaces one publication's prepared bindings without exposing them to serving queries.</summary>
    ValueTask PreparePublicationAsync(
        string publicationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This trigger-binding store does not support publication-scoped preparation."));

    /// <summary>Returns every prepared or active binding owned by one publication.</summary>
    ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByPublicationAsync(
        string publicationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<IReadOnlyCollection<WorkflowTriggerBinding>>(
            new NotSupportedException("This trigger-binding store does not support publication-scoped queries."));

    /// <summary>
    /// Makes one prepared publication visible and, when supplied, removes only the replaced publication from
    /// serving visibility. Rows are retained until publication-scoped cleanup.
    /// </summary>
    ValueTask ActivatePublicationAsync(
        string publicationId,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This trigger-binding store does not support publication-scoped activation."));

    /// <summary>Deletes every binding owned by one publication without affecting shared artifacts or other slots.</summary>
    ValueTask DeleteByPublicationAsync(string publicationId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This trigger-binding store does not support publication-scoped deletion."));

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

    /// <summary>
    /// Returns every binding of the given stimulus type across all artifacts. Used to rebuild a per-shell
    /// projection over one stimulus family (e.g. the HTTP route table, which enumerates every HTTP-endpoint
    /// binding's route template). Unlike <see cref="ListByStimulusAsync"/> this is not keyed by a stimulus
    /// hash — it is a type-scoped full scan, so callers should treat it as a startup/refresh operation rather
    /// than a per-request lookup.
    /// </summary>
    ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusTypeAsync(string stimulusType, CancellationToken cancellationToken = default);
}
