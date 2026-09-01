using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services;

public sealed class InMemoryWorkflowActivationAuthority : IWorkflowActivationAuthority
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, WorkflowActivationSlot> slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> activationSlots = new(StringComparer.Ordinal);

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
        ArgumentNullException.ThrowIfNull(request);
        var slotId = SlotId(request.WorkflowDefinitionId, request.SlotName, cancellationToken);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivationId);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedRevision);
        lock (gate)
        {
            var current = slots.GetValueOrDefault(slotId) ?? Empty(slotId, request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt);
            if (current.Revision != request.ExpectedRevision)
                return ValueTask.FromResult(Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation slot revision changed; another writer moved it first."));
            if (current.ActiveActivationId is not null && current.Source is not null &&
                request.OwnershipIntent != WorkflowActivationOwnershipIntent.TakeOver && !current.Source.IsSameOwnerAs(request.Source))
                return ValueTask.FromResult(Conflict(current, WorkflowActivationConflict.ForeignSource,
                    $"Definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}' is owned by activation source '{current.Source.Describe()}'; '{request.Source.Describe()}' cannot activate a different artifact on it. Ownership transfer is an explicit operator action."));
            if (activationSlots.TryGetValue(request.ActivationId, out var existing) && !StringComparer.Ordinal.Equals(existing, slotId))
                return ValueTask.FromResult(Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation is already live in another slot."));
            var replaced = current.ActiveActivationId;
            if (replaced is not null && !StringComparer.Ordinal.Equals(replaced, request.ActivationId)) activationSlots.Remove(replaced);
            var next = current with { ActiveActivationId = request.ActivationId, Source = request.Source, Revision = current.Revision + 1, UpdatedAt = request.UpdatedAt };
            slots[slotId] = next;
            activationSlots[request.ActivationId] = slotId;
            return ValueTask.FromResult(new WorkflowActivationTransition(true, next, replaced, ReplacedSource: current.Source));
        }
    }

    public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(string workflowDefinitionId, string slotName, WorkflowActivationSource source, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        lock (gate)
        {
            var current = slots.GetValueOrDefault(slotId) ?? Empty(slotId, workflowDefinitionId, slotName, updatedAt);
            if (current.Revision != expectedRevision) return ValueTask.FromResult(Conflict(current, WorkflowActivationConflict.RevisionMismatch, "The activation slot revision changed; another writer moved it first."));
            if (current.ActiveActivationId is not null && current.Source is not null && !current.Source.IsSameOwnerAs(source))
                return ValueTask.FromResult(Conflict(current, WorkflowActivationConflict.ForeignSource, $"Definition '{workflowDefinitionId}' slot '{slotName}' is owned by activation source '{current.Source.Describe()}'; '{source.Describe()}' cannot deactivate it."));
            var replaced = current.ActiveActivationId;
            if (replaced is not null) activationSlots.Remove(replaced);
            var next = current with { ActiveActivationId = null, Source = null, Revision = current.Revision + 1, UpdatedAt = updatedAt };
            slots[slotId] = next;
            return ValueTask.FromResult(new WorkflowActivationTransition(true, next, replaced, ReplacedSource: current.Source));
        }
    }

    private static string SlotId(string definitionId, string slotName, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return WorkflowActivationSlotIdentity.Create(definitionId, slotName); }
    private static WorkflowActivationSlot Empty(string id, string definitionId, string slotName, DateTimeOffset updatedAt) => new(id, definitionId, slotName, null, null, 0, updatedAt);
    private static WorkflowActivationTransition Conflict(WorkflowActivationSlot slot, WorkflowActivationConflict conflict, string diagnostic) => new(false, slot, Conflict: conflict, Diagnostic: diagnostic);
}
