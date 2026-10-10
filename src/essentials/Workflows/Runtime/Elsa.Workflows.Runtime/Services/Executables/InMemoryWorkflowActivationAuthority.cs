using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

public sealed class InMemoryWorkflowActivationAuthority : IWorkflowActivationAuthority
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, WorkflowActivationSlot> slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> activationSlots = new(StringComparer.Ordinal);

    /// <summary>The lock every slot read and transition takes; <see cref="InMemoryWorkflowActivationSwitch"/> holds it across a switch (#2230).</summary>
    internal Lock Gate => gate;

    public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
    {
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);
        lock (gate) return ValueTask.FromResult(slots.GetValueOrDefault(slotId));
    }

    public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowActivationSlot>>(slots.Values
                .Where(slot => StringComparer.Ordinal.Equals(slot.WorkflowDefinitionId, workflowDefinitionId))
                .OrderBy(slot => slot.SlotName, StringComparer.Ordinal).ToArray());
    }

    public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(Commit(PlanActivation(request)));
    }

    public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string workflowDefinitionId, string slotName, WorkflowActivationSource source, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) return ValueTask.FromResult(Commit(PlanDeactivation(new(workflowDefinitionId, slotName, source, expectedRevision, updatedAt))));
    }

    /// <summary>The transition <see cref="TryActivateAsync"/> would make, or its refusal; changes nothing. The caller holds <see cref="Gate"/>.</summary>
    internal WorkflowActivationTransition PlanActivation(WorkflowActivationSlotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var slotId = SlotId(request.WorkflowDefinitionId, request.SlotName, CancellationToken.None);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivationId);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedRevision);
        var current = slots.GetValueOrDefault(slotId) ?? Empty(slotId, request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt);
        if (current.Revision != request.ExpectedRevision)
            return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation slot revision changed; another writer moved it first.");
        if (current.ActiveActivationId is not null && current.Source is not null &&
            request.OwnershipIntent != WorkflowActivationOwnershipIntent.TakeOver && !current.Source.IsSameOwnerAs(request.Source))
            return Conflict(current, WorkflowActivationConflict.ForeignSource,
                $"Definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}' is owned by activation source '{current.Source.Describe()}'; '{request.Source.Describe()}' cannot activate a different artifact on it. Ownership transfer is an explicit operator action.");
        if (activationSlots.TryGetValue(request.ActivationId, out var existing) && !StringComparer.Ordinal.Equals(existing, slotId))
            return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation is already live in another slot.");
        var next = current with { ActiveActivationId = request.ActivationId, Source = request.Source, Revision = current.Revision + 1, UpdatedAt = request.UpdatedAt };
        return new WorkflowActivationTransition(true, next, current.ActiveActivationId, ReplacedSource: current.Source);
    }

    /// <summary>The transition <see cref="TryDeactivateAsync"/> would make, or its refusal; changes nothing. The caller holds <see cref="Gate"/>.</summary>
    internal WorkflowActivationTransition PlanDeactivation(WorkflowDeactivationSlotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        var slotId = SlotId(request.WorkflowDefinitionId, request.SlotName, CancellationToken.None);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedRevision);
        var current = slots.GetValueOrDefault(slotId) ?? Empty(slotId, request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt);
        if (current.Revision != request.ExpectedRevision) return Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation slot revision changed; another writer moved it first.");
        if (current.ActiveActivationId is not null && current.Source is not null && !current.Source.IsSameOwnerAs(request.Source))
            return Conflict(current, WorkflowActivationConflict.ForeignSource, $"Definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}' is owned by activation source '{current.Source.Describe()}'; '{request.Source.Describe()}' cannot deactivate it.");
        var next = current with { ActiveActivationId = null, Source = null, Revision = current.Revision + 1, UpdatedAt = request.UpdatedAt };
        return new WorkflowActivationTransition(true, next, current.ActiveActivationId, ReplacedSource: current.Source);
    }

    /// <summary>The slot as it stands, or <see langword="null"/> when it was never written. The caller holds <see cref="Gate"/>.</summary>
    internal WorkflowActivationSlot? Current(string workflowDefinitionId, string slotName) =>
        slots.GetValueOrDefault(SlotId(workflowDefinitionId, slotName, CancellationToken.None));

    /// <summary>Records a transition a plan allowed, and returns it; a refusal changes nothing. The caller holds <see cref="Gate"/>.</summary>
    internal WorkflowActivationTransition Commit(WorkflowActivationTransition transition)
    {
        if (!transition.Succeeded)
            return transition;
        var slot = transition.Slot;
        if (transition.ReplacedActivationId is { } replaced && !StringComparer.Ordinal.Equals(replaced, slot.ActiveActivationId))
            activationSlots.Remove(replaced);
        if (slot.ActiveActivationId is { } active)
            activationSlots[active] = slot.SlotId;
        slots[slot.SlotId] = slot;
        return transition;
    }

    private static string SlotId(string definitionId, string slotName, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return WorkflowActivationSlotIdentity.Create(definitionId, slotName); }
    private static WorkflowActivationSlot Empty(string id, string definitionId, string slotName, DateTimeOffset updatedAt) => new(id, definitionId, slotName, null, null, 0, updatedAt);
    private static WorkflowActivationTransition Conflict(WorkflowActivationSlot slot, WorkflowActivationConflict conflict, string diagnostic) => new(false, slot, Conflict: conflict, Diagnostic: diagnostic);
}
