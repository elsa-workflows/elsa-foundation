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

    /// <summary>Atomically replaces one activation's prepared bindings without exposing them to serving queries.</summary>
    ValueTask PrepareActivationAsync(
        string activationId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This trigger-binding store does not support activation-scoped preparation."));

    /// <summary>
    /// Returns one finite, deterministically ordered page of prepared or active bindings owned by one
    /// activation. Callers whose business semantics require the complete activation projection must
    /// deliberately traverse the opaque continuation.
    /// </summary>
    ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(
        WorkflowTriggerBindingActivationPageQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<WorkflowTriggerBindingPage>(
            new NotSupportedException("This trigger-binding store does not support activation-scoped queries."));

    /// <summary>
    /// Makes one prepared activation visible and, when supplied, removes only the replaced activation from
    /// serving visibility. Rows are retained until activation-scoped cleanup.
    /// </summary>
    ValueTask ActivateAsync(
        string activationId,
        string? replacedActivationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This trigger-binding store does not support activation-scoped activation."));

    /// <summary>Deletes every binding owned by one activation without affecting shared artifacts or other slots.</summary>
    ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("This trigger-binding store does not support activation-scoped deletion."));

    /// <summary>
    /// Removes every binding owned by the given artifact. Returns the number of bindings removed. Used on
    /// republish to clear a prior version's triggers before the current version's triggers are written.
    /// </summary>
    ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-artifact lookup used by the stimulus router: returns one finite page of active start-trigger
    /// bindings whose exact stimulus identity matches. Continue until the returned token is null when the
    /// operation requires every matching workflow.
    /// </summary>
    ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(
        WorkflowTriggerBindingPageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one finite, deterministically ordered page of bindings owned by the given artifact.
    /// Callers rebuilding or replacing an entire artifact projection must deliberately traverse the
    /// opaque continuation.
    /// </summary>
    ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(
        WorkflowTriggerBindingArtifactPageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one finite page of active bindings of the given stimulus type across all artifacts. Callers
    /// rebuilding a complete projection must traverse the opaque continuation deliberately.
    /// </summary>
    ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(
        WorkflowTriggerBindingTypePageQuery query,
        CancellationToken cancellationToken = default);
}
