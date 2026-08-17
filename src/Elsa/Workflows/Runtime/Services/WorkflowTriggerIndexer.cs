using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IWorkflowTriggerIndexer"/> (W7, E3-1). It extracts the trigger bindings for a workflow
/// executable and persists them as one activation's <b>prepared</b> (non-serving) projection, keyed by the
/// activation and slot the coordinator supplies.
/// </summary>
/// <remarks>
/// <para>
/// Invoked by <see cref="IWorkflowActivationCoordinator"/>; any failure propagates and fails the activation (no
/// silently unindexed serving trigger). After extraction — and BEFORE any write — it runs every registered
/// <see cref="IWorkflowTriggerIndexValidator"/> over the extracted binding set, so a constraint violation (e.g.
/// HTTP's cross-definition (template, method) uniqueness) fails the activation with the durable index untouched.
/// </para>
/// <para>
/// It writes <b>only</b> the candidate activation's rows: nothing here deletes by artifact, so a second slot or
/// a predecessor activation sharing the same artifact keeps its projection. Supersession is the coordinator's
/// job, performed through the store's activation-scoped activate/delete, and
/// <see cref="IWorkflowTriggerIndexObserver"/> notification belongs to the coordinator too — prepared rows do
/// not serve, so notifying from here would project routes that are not live.
/// </para>
/// </remarks>
public sealed class WorkflowTriggerIndexer : IWorkflowTriggerIndexer
{
    private readonly IWorkflowTriggerBindingExtractor _extractor;
    private readonly IWorkflowTriggerBindingStore _store;
    private readonly IEnumerable<IWorkflowTriggerIndexValidator> _validators;

    public WorkflowTriggerIndexer(
        IWorkflowTriggerBindingExtractor extractor,
        IWorkflowTriggerBindingStore store,
        IEnumerable<IWorkflowTriggerIndexValidator>? validators = null)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(store);
        _extractor = extractor;
        _store = store;
        _validators = validators ?? [];
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
        await _store.PrepareActivationAsync(activationId, bindings, cancellationToken);
        return bindings;
    }

    private async ValueTask ValidateAsync(
        string artifactId,
        IReadOnlyCollection<WorkflowTriggerBinding> bindings,
        CancellationToken cancellationToken)
    {
        var snapshot = new WorkflowTriggerIndexSnapshot(artifactId, bindings);

        // Validate BEFORE any write (issue #592 item 2): a constraint violation must fail the activation with
        // the store untouched — a post-write throw would leave the conflicting bindings durable, poisoning every
        // later activation and startup that reads the index. Exceptions propagate and fail the activation.
        foreach (var validator in _validators)
            await validator.ValidateAsync(snapshot, cancellationToken);
    }
}
