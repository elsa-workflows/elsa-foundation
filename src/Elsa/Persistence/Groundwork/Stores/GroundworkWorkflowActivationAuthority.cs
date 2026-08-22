using Elsa.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Durable <see cref="IWorkflowActivationAuthority"/> for the Groundwork bridge (FR-B-006): the definition-keyed
/// ledger of which activation is live per <c>(DefinitionId, SlotName)</c>. Registered whenever runtime Groundwork
/// persistence is composed, exactly like the trigger-binding and recurring-schedule bridges — without this swap
/// the activation ledger lives only in process memory, so a restart forgets which activation is serving even
/// though its bindings and schedules are durable.
/// </summary>
/// <remarks>
/// <para>
/// <b>Semantics are the in-memory authority's, verbatim.</b> <c>InMemoryWorkflowActivationAuthority</c> is the
/// reference implementation and the shared behaviour contract; every rule below mirrors it deliberately so that
/// composing durable persistence never changes an activation outcome: revision 0 with no active activation is the
/// canonical "never activated" state (a first activation is an ordinary CAS from 0, not a create path), ownership
/// is read from the slot's <see cref="WorkflowActivationSlot.Source"/> field only — never from activation-id
/// prefixes — comparing both kind and source id, one activation is live in at most one slot, and the replaced
/// activation id is returned so the coordinator can retire its projections. The diagnostics are worded identically
/// so a caller cannot tell the two apart.
/// </para>
/// <para>
/// <b>Two guards, one meaning.</b> The revision check is evaluated against the loaded slot, and the document's own
/// Groundwork version is presented as the expected version on write. A concurrent writer that moves the slot
/// between the read and the write therefore loses the write rather than clobbering it, and is reported as the same
/// <see cref="WorkflowActivationConflict.RevisionMismatch"/> the in-memory authority raises on a stale revision.
/// </para>
/// </remarks>
public sealed class GroundworkWorkflowActivationAuthority(
    IDocumentStore store,
    IGroundworkRuntimeDocumentSerializer serializer,
    IBoundedDocumentStore? boundedStore = null)
    : GroundworkDocumentStore(store, serializer, ElsaRuntimeStorageManifest.WorkflowActivationSlotDocumentKind, boundedStore),
        IWorkflowActivationAuthority
{
    public async ValueTask<WorkflowActivationSlot?> FindAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default)
    {
        var slotId = SlotId(workflowDefinitionId, slotName, cancellationToken);

        try
        {
            return await LoadSlotAsync(slotId, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            throw Wrap(workflowDefinitionId, slotName, "read", exception);
        }
    }

    public async ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var slots = await QueryDocumentsAsync<ActivationSlotDocument, WorkflowActivationSlot>(
                ElsaRuntimeStorageManifest.ListWorkflowActivationSlotsByDefinitionQuery,
                ElsaRuntimeStorageManifest.WorkflowActivationSlotDefinitionIdField,
                workflowDefinitionId,
                document => document.Slot,
                cancellationToken);
            return slots.OrderBy(slot => slot.SlotName, StringComparer.Ordinal).ToArray();
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            throw Wrap(workflowDefinitionId, "*", "list", exception);
        }
    }

    public async ValueTask<WorkflowActivationTransition> TryActivateAsync(
        WorkflowActivationSlotRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var slotId = SlotId(request.WorkflowDefinitionId, request.SlotName, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivationId);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedRevision);

        try
        {
            var loaded = await LoadEnvelopeAsync(slotId, cancellationToken);
            var current = loaded?.Slot ?? CurrentOrEmpty(slotId, request.WorkflowDefinitionId, request.SlotName, request.UpdatedAt);

            if (current.Revision != request.ExpectedRevision)
                return Conflict(
                    current,
                    WorkflowActivationConflict.RevisionMismatch,
                    "The activation slot revision changed; another writer moved it first.");

            if (!IsClaimableBy(current, request.Source, request.OwnershipIntent))
                return Conflict(
                    current,
                    WorkflowActivationConflict.ForeignSource,
                    $"Definition '{request.WorkflowDefinitionId}' slot '{request.SlotName}' is owned by activation source " +
                    $"'{current.Source!.Describe()}'; '{request.Source.Describe()}' cannot activate a different artifact on it. " +
                    "Ownership transfer is an explicit operator action.");

            if (await IsLiveInAnotherSlotAsync(request.ActivationId, slotId, cancellationToken))
                return Conflict(
                    current,
                    WorkflowActivationConflict.RevisionMismatch,
                    "The activation is already live in another slot.");

            var next = current with
            {
                ActiveActivationId = request.ActivationId,
                Source = request.Source,
                Revision = current.Revision + 1,
                UpdatedAt = request.UpdatedAt
            };

            return await CommitAsync(next, current, loaded?.Version, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            throw Wrap(request.WorkflowDefinitionId, request.SlotName, "activate", exception);
        }
    }

    public async ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
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

        try
        {
            var loaded = await LoadEnvelopeAsync(slotId, cancellationToken);
            var current = loaded?.Slot ?? CurrentOrEmpty(slotId, workflowDefinitionId, slotName, updatedAt);

            if (current.Revision != expectedRevision)
                return Conflict(
                    current,
                    WorkflowActivationConflict.RevisionMismatch,
                    "The activation slot revision changed; another writer moved it first.");

            if (!IsOwnedBy(current, source))
                return Conflict(
                    current,
                    WorkflowActivationConflict.ForeignSource,
                    $"Definition '{workflowDefinitionId}' slot '{slotName}' is owned by activation source " +
                    $"'{current.Source!.Describe()}'; '{source.Describe()}' cannot deactivate it.");

            var next = current with
            {
                ActiveActivationId = null,
                Source = null,
                Revision = current.Revision + 1,
                UpdatedAt = updatedAt
            };

            return await CommitAsync(next, current, loaded?.Version, cancellationToken);
        }
        catch (Exception exception) when (NotRequestedCancellation(exception, cancellationToken))
        {
            throw Wrap(workflowDefinitionId, slotName, "deactivate", exception);
        }
    }

    /// <summary>
    /// Writes the transition under the document version that was read. Losing that race is reported as the same
    /// stale-revision refusal a caller would have seen had the other writer landed first.
    /// </summary>
    private async ValueTask<WorkflowActivationTransition> CommitAsync(
        WorkflowActivationSlot next,
        WorkflowActivationSlot current,
        long? expectedDocumentVersion,
        CancellationToken cancellationToken)
    {
        var result = await SaveDocumentAsync(
            next.SlotId,
            ToDocument(next),
            cancellationToken,
            expectedDocumentVersion ?? 0);

        if (result.Status != DocumentStoreWriteStatus.Saved)
        {
            var winner = await LoadSlotAsync(next.SlotId, cancellationToken) ?? current;
            return Conflict(
                winner,
                WorkflowActivationConflict.RevisionMismatch,
                "The activation slot revision changed; another writer moved it first.");
        }

        // Returned unconditionally, matching the in-memory authority: the coordinator decides whether the
        // replaced id is worth retiring (it skips the no-op case where it equals the incoming activation). The
        // displaced owner rides along for the same reason — compensation has to restore it, not just the id.
        return new WorkflowActivationTransition(true, next, current.ActiveActivationId, ReplacedSource: current.Source);
    }

    private async ValueTask<bool> IsLiveInAnotherSlotAsync(
        string activationId,
        string slotId,
        CancellationToken cancellationToken)
    {
        var owners = await QueryDocumentsAsync<ActivationSlotDocument, string>(
            ElsaRuntimeStorageManifest.FindWorkflowActivationSlotByActiveActivationQuery,
            ElsaRuntimeStorageManifest.WorkflowActivationSlotActiveActivationIdField,
            activationId,
            document => document.Slot.SlotId,
            cancellationToken);
        return owners.Any(ownerSlotId => !StringComparer.Ordinal.Equals(ownerSlotId, slotId));
    }

    private ValueTask<WorkflowActivationSlot?> LoadSlotAsync(string slotId, CancellationToken cancellationToken) =>
        LoadDocumentAsync<ActivationSlotDocument, WorkflowActivationSlot>(
            slotId,
            document => document.Slot,
            cancellationToken);

    private async ValueTask<(WorkflowActivationSlot Slot, long Version)?> LoadEnvelopeAsync(
        string slotId,
        CancellationToken cancellationToken)
    {
        var envelope = await Store.LoadAsync(DocumentKind, slotId, cancellationToken);
        return envelope is null
            ? null
            : (Serializer.Deserialize<ActivationSlotDocument>(envelope).Slot, envelope.Version);
    }

    /// <summary>
    /// An unowned slot (nothing live yet) is claimable by any source; an owned slot only by its owner.
    /// Read from the slot's <see cref="WorkflowActivationSlot.Source"/> field — never from id prefixes.
    /// </summary>
    private static bool IsOwnedBy(WorkflowActivationSlot slot, WorkflowActivationSource source) =>
        slot.ActiveActivationId is null || slot.Source is null || slot.Source.IsSameOwnerAs(source);

    /// <summary>
    /// Activation's claim rule: ownership, or an explicit takeover intent that overrides it (T118). Mirrors the
    /// in-memory authority exactly — the intent is honoured for any requesting source, so the durable ledger, like
    /// the in-memory one, never learns which callers exist.
    /// </summary>
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

    private static string SlotId(string workflowDefinitionId, string slotName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WorkflowActivationSlotIdentity.Create(workflowDefinitionId, slotName);
    }

    private static bool NotRequestedCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested;

    private static GroundworkWorkflowActivationAuthorityException Wrap(
        string workflowDefinitionId,
        string slotName,
        string operation,
        Exception exception) =>
        new(
            workflowDefinitionId,
            slotName,
            $"The durable activation ledger could not {operation} the slot of workflow definition " +
            $"'{workflowDefinitionId}' lane '{slotName}'.",
            exception);

    // Ownership and the live-activation guard are hot lookups, so the definition id and the live activation id
    // are lifted to flat top-level fields instead of being reached through a nested payload path — the same
    // shape the other list-capable runtime bridges use. A cleared slot omits the activation id entirely, and the
    // guard only ever asks for equality with a real activation id, so a cleared slot is "not live anywhere"
    // whatever the serving index does with the missing value.
    private sealed record ActivationSlotDocument(
        string WorkflowDefinitionId,
        string? ActiveActivationId,
        WorkflowActivationSlot Slot);

    private static ActivationSlotDocument ToDocument(WorkflowActivationSlot slot) =>
        new(slot.WorkflowDefinitionId, slot.ActiveActivationId, slot);
}
