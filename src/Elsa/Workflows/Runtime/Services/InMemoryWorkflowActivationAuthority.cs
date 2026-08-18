using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// Non-durable <see cref="IWorkflowActivationAuthority"/>, registered by <c>AddWorkflowRuntime()</c>
/// as the fallback when no Groundwork runtime persistence is composed.
/// </summary>
/// <remarks>
/// Transition semantics are carried over verbatim from publishing's in-memory slot store so the
/// behaviour under test is unchanged by the supersession (§2.21.1): CAS on revision, one activation
/// live in at most one slot, and the replaced activation returned so the caller can retire its
/// projections. The ownership rule is the one genuine addition (FR-B-006).
/// </remarks>
public sealed class InMemoryWorkflowActivationAuthority : IWorkflowActivationAuthority
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, WorkflowActivationSlot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _activationSlots = new(StringComparer.Ordinal);

    public ValueTask<WorkflowActivationSlot?> FindAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default)
    {
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);
        lock (_gate)
            return ValueTask.FromResult(_slots.GetValueOrDefault(slotId));
    }

    public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyCollection<WorkflowActivationSlot>>(_slots.Values
                .Where(slot => StringComparer.Ordinal.Equals(slot.WorkflowDefinitionId, workflowDefinitionId))
                .OrderBy(slot => slot.SlotName, StringComparer.Ordinal)
                .ToArray());
    }

    public ValueTask<WorkflowActivationTransition> TryActivateAsync(
        WorkflowActivationSlotRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var slotId = SlotId(request.WorkflowDefinitionId, request.SlotName, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivationId);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedRevision);

        lock (_gate)
        {
            var current = CurrentOrEmptyLocked(slotId, request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt);

            if (current.Revision != request.ExpectedRevision)
                return ValueTask.FromResult(Conflict(
                    current,
                    WorkflowActivationConflict.RevisionMismatch,
                    "The activation slot revision changed; another writer moved it first."));

            if (!IsClaimableBy(current, request.Source, request.OwnershipIntent))
                return ValueTask.FromResult(Conflict(
                    current,
                    WorkflowActivationConflict.ForeignSource,
                    $"Definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}' is owned by activation source " +
                    $"'{current.Source!.Describe()}'; '{request.Source.Describe()}' cannot activate a different artifact on it. " +
                    "Ownership transfer is an explicit operator action."));

            if (_activationSlots.TryGetValue(request.ActivationId, out var existingSlotId) &&
                !StringComparer.Ordinal.Equals(existingSlotId, slotId))
                return ValueTask.FromResult(Conflict(
                    current,
                    WorkflowActivationConflict.RevisionMismatch,
                    "The activation is already live in another slot."));

            var replacedActivationId = current.ActiveActivationId;
            if (replacedActivationId is not null && !StringComparer.Ordinal.Equals(replacedActivationId, request.ActivationId))
                _activationSlots.Remove(replacedActivationId);

            var next = current with
            {
                ActiveActivationId = request.ActivationId,
                Source = request.Source,
                Revision = current.Revision + 1,
                UpdatedAt = request.UpdatedAt
            };
            _slots[slotId] = next;
            _activationSlots[request.ActivationId] = slotId;
            return ValueTask.FromResult(new WorkflowActivationTransition(
                true,
                next,
                replacedActivationId,
                ReplacedSource: current.Source));
        }
    }

    public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        string workflowDefinitionId,
        string slotName,
        WorkflowActivationSource source,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);

        lock (_gate)
        {
            var current = CurrentOrEmptyLocked(slotId, workflowDefinitionId, slotName, updatedAt);

            if (current.Revision != expectedRevision)
                return ValueTask.FromResult(Conflict(
                    current,
                    WorkflowActivationConflict.RevisionMismatch,
                    "The activation slot revision changed; another writer moved it first."));

            if (!IsOwnedBy(current, source))
                return ValueTask.FromResult(Conflict(
                    current,
                    WorkflowActivationConflict.ForeignSource,
                    $"Definition '{workflowDefinitionId}' slot '{slotName}' is owned by activation source " +
                    $"'{current.Source!.Describe()}'; '{source.Describe()}' cannot deactivate it."));

            var replacedActivationId = current.ActiveActivationId;
            if (replacedActivationId is not null)
                _activationSlots.Remove(replacedActivationId);

            var next = current with
            {
                ActiveActivationId = null,
                Source = null,
                Revision = current.Revision + 1,
                UpdatedAt = updatedAt
            };
            _slots[slotId] = next;
            return ValueTask.FromResult(new WorkflowActivationTransition(
                true,
                next,
                replacedActivationId,
                ReplacedSource: current.Source));
        }
    }

    /// <summary>
    /// An unowned slot (nothing live yet) is claimable by any source; an owned slot only by its owner.
    /// Read from the slot's <see cref="WorkflowActivationSlot.Source"/> field — never from id prefixes.
    /// </summary>
    private static bool IsOwnedBy(WorkflowActivationSlot slot, WorkflowActivationSource source) =>
        slot.ActiveActivationId is null || slot.Source is null || slot.Source.IsSameOwnerAs(source);

    /// <summary>
    /// Activation's claim rule: ownership, or an explicit takeover intent that overrides it (T118).
    /// </summary>
    /// <remarks>
    /// The intent is honoured for <b>any</b> requesting source, which is the point — the authority never learns
    /// which callers exist. Two reconciliation sources still refuse each other because neither declares a
    /// takeover, not because the ledger knows what a reconciliation source is.
    /// </remarks>
    private static bool IsClaimableBy(
        WorkflowActivationSlot slot,
        WorkflowActivationSource source,
        WorkflowActivationOwnershipIntent intent) =>
        intent == WorkflowActivationOwnershipIntent.TakeOver || IsOwnedBy(slot, source);

    private static WorkflowActivationTransition Conflict(
        WorkflowActivationSlot slot,
        WorkflowActivationConflict conflict,
        string diagnostic) =>
        new(false, slot, null, conflict, diagnostic);

    private static WorkflowActivationSlot CurrentOrEmpty(
        string slotId,
        string workflowDefinitionId,
        string slotName,
        DateTimeOffset updatedAt) =>
        // Revision 0 with no active activation is the canonical "never activated" state, so a first
        // activation is an ordinary CAS from 0 rather than a special create path.
        new(slotId, workflowDefinitionId, slotName, null, null, 0, updatedAt);

    private WorkflowActivationSlot CurrentOrEmptyLocked(
        string slotId,
        string workflowDefinitionId,
        string slotName,
        DateTimeOffset updatedAt) =>
        _slots.GetValueOrDefault(slotId) ?? CurrentOrEmpty(slotId, workflowDefinitionId, slotName, updatedAt);

    private static string SlotId(string workflowDefinitionId, string slotName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WorkflowActivationSlotIdentity.Create(workflowDefinitionId, slotName);
    }
}
