using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>
/// Default <see cref="IWorkflowTriggerIndexer"/>. It extracts the trigger bindings for a published
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
    internal IRecurringTriggerScheduleStore? EmptyRecurringProjection { get; init; }
    internal IEnumerable<IRecurringTriggerScheduleProvider> RecurringScheduleProviders { get; init; } = [];

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

    public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
        WorkflowExecutable executable,
        string activationId,
        string slotId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);

        var bindings = _extractor.Evaluate(executable).Bindings
            .Select(binding => binding with
            {
                TriggerBindingId = WorkflowTriggerBinding.BuildId(
                    activationId,
                    binding.ArtifactId,
                    binding.ExecutableNodeId,
                    binding.StimulusHash),
                ActivationId = activationId,
                SlotId = slotId,
                IsActive = false
            })
            .ToArray();

        await ValidateAsync(executable.Identity.ArtifactId, bindings, cancellationToken);
        if (EmptyRecurringProjection is not null)
            EnsureNoRecurringStarts(executable, cancellationToken);
        await _store.PrepareActivationAsync(activationId, bindings, cancellationToken);
        if (EmptyRecurringProjection is not null)
            await EmptyRecurringProjection.PrepareActivationAsync(activationId, [], cancellationToken);
        return bindings;
    }

    private void EnsureNoRecurringStarts(WorkflowExecutable executable, CancellationToken cancellationToken)
    {
        var providers = RecurringScheduleProviders.ToArray();
        if (providers.Length == 0)
            return;

        foreach (var node in executable.RootActivity.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!node.Metadata.TryGetValue(TriggerNodeMetadata.ExecutionTypeKey, out var executionType) ||
                executionType != TriggerNodeMetadata.TriggerExecutionType)
                continue;

            foreach (var provider in providers)
            {
                try
                {
                    if (provider.Describe(node).Count == 0)
                        continue;
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
                {
                    throw new WorkflowTriggerPreflightException(executable.Identity.ArtifactId, node.ExecutableNodeId,
                        node.ActivityType, [provider.ProviderId], "RecurringSchedule",
                        "The recurring start schedule could not be described.", exception);
                }

                throw new WorkflowTriggerPreflightException(executable.Identity.ArtifactId, node.ExecutableNodeId,
                    node.ActivityType, [provider.ProviderId], "RecurringSchedule",
                    "Publishing a recurring start requires a recurring schedule indexer.");
            }
        }
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
