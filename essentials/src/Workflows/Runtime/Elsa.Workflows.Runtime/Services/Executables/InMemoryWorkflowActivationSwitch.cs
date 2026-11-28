using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// The in-memory <see cref="IWorkflowActivationSwitch"/> (#2230). It holds the lock of the in-memory slot authority and
/// of each in-memory projection store together, in that order, so every reader sees a slot transition and its projection
/// switch as one step.
/// </summary>
/// <remarks>
/// <para>
/// Source references are moved through their store, by compare-and-swap, after the locked step or before it, in an order
/// that keeps every state between the two safe: an activation never serves with a retired reference. A replaced
/// activation's reference is retired once it no longer serves; a reverted one's, or a candidate's that a discard retired,
/// is restored before it serves again, and put back exactly as it was if the switch is refused; a discarded or reverted
/// candidate's is retired once nothing can switch it on.
/// Once the locked step is made, the switch is made, so those moves use <see cref="CancellationToken.None"/>.
/// </para>
/// <para>
/// It refuses, when constructed, a slot authority or a projection store that is not the in-memory one, because it could
/// not hold that store's state with the others: compose a backend's switch with that backend's stores.
/// </para>
/// </remarks>
public sealed class InMemoryWorkflowActivationSwitch : IDefaultWorkflowActivationSwitch
{
    internal PublicationActivationSwitch Inner { get; }

    public InMemoryWorkflowActivationSwitch(
        IWorkflowActivationAuthority authority,
        IWorkflowExecutableSourceReferenceStore references,
        TimeProvider timeProvider,
        IWorkflowTriggerBindingStore? triggerBindingStore = null,
        IRecurringTriggerScheduleStore? recurringScheduleStore = null)
        : this(authority, references, timeProvider, null, null, triggerBindingStore, recurringScheduleStore) { }

    public InMemoryWorkflowActivationSwitch(IWorkflowActivationAuthority authority,
        IWorkflowExecutableSourceReferenceStore references, TimeProvider timeProvider,
        InMemoryRuntimeCheckpointStoreState? reservationState,
        IPersistenceAccessContextAccessor? reservationAccess, IWorkflowTriggerBindingStore? triggerBindingStore = null,
        IRecurringTriggerScheduleStore? recurringScheduleStore = null)
        : this(authority, references, timeProvider, reservationState, reservationAccess, [], triggerBindingStore, recurringScheduleStore) { }

    public InMemoryWorkflowActivationSwitch(IWorkflowActivationAuthority authority,
        IWorkflowExecutableSourceReferenceStore references, TimeProvider timeProvider,
        InMemoryRuntimeCheckpointStoreState? reservationState,
        IPersistenceAccessContextAccessor? reservationAccess, IEnumerable<IIngressReservationPolicy> reservationPolicies,
        IWorkflowTriggerBindingStore? triggerBindingStore = null, IRecurringTriggerScheduleStore? recurringScheduleStore = null)
    {
        Inner = new PublicationActivationSwitch(authority, references, timeProvider, reservationState?.PublicationReservations,
            reservationAccess, reservationPolicies, triggerBindingStore, recurringScheduleStore);
    }

    public ValueTask<WorkflowActivationTransition> TryActivateAsync(
        WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
        Inner.TryActivateAsync(request, cancellationToken);

    public ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default) =>
        Inner.TryRevertAsync(revert, cancellationToken);

    public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        WorkflowDeactivationSlotRequest request, IReadOnlyCollection<string> alsoServing, CancellationToken cancellationToken = default) =>
        Inner.TryDeactivateAsync(request, alsoServing, cancellationToken);

    public ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) =>
        Inner.TryDiscardAsync(reference, cancellationToken);

    public ValueTask<bool> TryRepairAsync(
        WorkflowActivationSlot slot, IReadOnlyCollection<string> alsoServing, CancellationToken cancellationToken = default) =>
        Inner.TryRepairAsync(slot, alsoServing, cancellationToken);
}
