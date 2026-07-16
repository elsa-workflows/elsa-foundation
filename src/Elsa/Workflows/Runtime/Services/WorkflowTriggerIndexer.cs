using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IWorkflowTriggerIndexer"/> (W7, E3-1). It extracts the trigger bindings for a published
/// executable and replaces the artifact's prior bindings with the current set: it first deletes every binding
/// owned by the artifact, then writes the freshly extracted ones, so a republished version's triggers fully
/// supersede the previous version's and no stale trigger from an earlier version can still start a workflow.
/// </summary>
/// <remarks>
/// Invoked inside the publish flow; any failure propagates and fails the publish (no silently unindexed
/// trigger). The delete-then-write sequence is idempotent, so retrying a failed publish converges. After
/// extraction — and BEFORE any write — it runs every registered <see cref="IWorkflowTriggerIndexValidator"/>
/// over the extracted binding set, so a publish-time constraint violation (e.g. HTTP's cross-definition
/// (template, method) uniqueness) fails the publish with the durable index untouched. After the write succeeds
/// — and before returning — it notifies every registered <see cref="IWorkflowTriggerIndexObserver"/> with the
/// artifact's new bindings so index-derived projections (e.g. the HTTP route table) refresh as part of the
/// same publish; an observer that throws also fails the publish (same "indexing failure fails the publish"
/// rule).
/// </remarks>
public sealed class WorkflowTriggerIndexer : IWorkflowTriggerIndexer
{
    private readonly IWorkflowTriggerBindingExtractor _extractor;
    private readonly IWorkflowTriggerBindingStore _store;
    private readonly IEnumerable<IWorkflowTriggerIndexObserver> _observers;
    private readonly IEnumerable<IWorkflowTriggerIndexValidator> _validators;

    public WorkflowTriggerIndexer(
        IWorkflowTriggerBindingExtractor extractor,
        IWorkflowTriggerBindingStore store,
        IEnumerable<IWorkflowTriggerIndexObserver>? observers = null,
        IEnumerable<IWorkflowTriggerIndexValidator>? validators = null)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(store);
        _extractor = extractor;
        _store = store;
        _observers = observers ?? [];
        _validators = validators ?? [];
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);

        // Extract first: an unroutable trigger throws here, before any write, so a bad publish fails cleanly.
        var bindings = _extractor.Evaluate(executable).Bindings;
        var snapshot = await ValidateAsync(executable.Identity.ArtifactId, bindings, cancellationToken);

        await _store.DeleteByArtifactAsync(executable.Identity.ArtifactId, cancellationToken);

        foreach (var binding in bindings)
            await _store.SaveAsync(binding, cancellationToken);

        // Notify projections after the write, before returning. Exceptions propagate and fail the publish.
        foreach (var observer in _observers)
            await observer.OnTriggersIndexedAsync(snapshot, cancellationToken);

        return bindings;
    }

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PreparePublicationAsync(
        WorkflowExecutable executable,
        string publicationId,
        string slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        var bindings = _extractor.Evaluate(executable).Bindings
            .Select(binding => binding with
            {
                TriggerBindingId = WorkflowTriggerBinding.BuildId(
                    publicationId,
                    binding.ArtifactId,
                    binding.ExecutableNodeId,
                    binding.StimulusHash),
                PublicationId = publicationId,
                SlotId = slotId,
                IsActive = false
            })
            .ToArray();

        await ValidateAsync(executable.Identity.ArtifactId, bindings, cancellationToken);
        await _store.PreparePublicationAsync(publicationId, bindings, cancellationToken);
        return bindings;
    }

    private async ValueTask<WorkflowTriggerIndexSnapshot> ValidateAsync(
        string artifactId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken)
    {
        var snapshot = new WorkflowTriggerIndexSnapshot(artifactId, bindings);

        // Validate BEFORE any write (issue #592 item 2): a constraint violation must fail the publish with the
        // store untouched — a post-write throw would leave the conflicting bindings durable, poisoning every
        // later publish and startup that reads the index. Exceptions propagate and fail the publish.
        foreach (var validator in _validators)
            await validator.ValidateAsync(snapshot, cancellationToken);
        return snapshot;
    }
}
