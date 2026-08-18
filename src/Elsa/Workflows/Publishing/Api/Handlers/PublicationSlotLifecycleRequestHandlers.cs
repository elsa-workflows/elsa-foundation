using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>
/// Retracts a publication: publishing's own bookkeeping wrapped around one call to the shared
/// <see cref="IWorkflowActivationCoordinator"/>.
/// </summary>
/// <remarks>
/// The mirror of <see cref="PublicationActivator"/>, and deliberately shaped the same way (T121). It holds
/// <b>no</b> copy of the retraction sequence: the slot transition, the serving projections' removal, the observer
/// notification and the compensation that puts all of them back belong to the coordinator, which owns the same
/// machinery in the activating direction. What stays here is publishing's: deciding whether this activation is
/// publishing's to withdraw at all, retiring the <c>PublicationRecord</c>, and retiring the source reference.
/// </remarks>
public sealed class UnpublishPublicationSlotRequestHandler(
    IWorkflowActivationAuthority activationAuthority,
    IWorkflowActivationCoordinator activationCoordinator,
    IPublicationRecordStore publicationStore,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider,
    ILogger<UnpublishPublicationSlotRequestHandler>? logger = null) : IRequestHandler<UnpublishPublicationSlot, WorkflowActivationSlot>
{
    public async Task<WorkflowActivationSlot> Handle(UnpublishPublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await activationAuthority.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' does not exist.");
        if (slot.ActiveActivationId is not { } publicationId)
            return slot;

        // FR-B-006 / the 2026-08-16 publish/activation split: unpublish retracts a PUBLICATION, so it can only
        // speak for a slot publishing owns. Ownership is read from the slot's explicit WorkflowActivationSource
        // (P3) BEFORE the journal is consulted, because a slot activated by artifact reconciliation legitimately
        // holds an ActiveActivationId with no PublicationRecord behind it. That absence is the answer "not
        // published by me", never a missing-data fault — and the journal is checked in the same breath rather
        // than dereferenced, since without a record there is genuinely nothing for unpublish to retract. Both
        // roads lead to the same honest reply: this activation is not publishing's to withdraw.
        var publication = IsOwnedByPublishing(slot)
            ? await publicationStore.FindAsync(publicationId, cancellationToken)
            : null;
        if (publication is null)
            throw new PublicationActivationException(ForeignActivationFailure(slot, publicationId));

        // The artifact is required by the coordinator's COMPENSATION path, which re-prepares the serving
        // projections from it if the removal does not finish. Resolving it up front means a missing artifact
        // fails the unpublish before the slot moves, rather than stranding a retracted slot that cannot be undone.
        var executable = await executableStore.FindAsync(publication.ArtifactId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Executable artifact '{publication.ArtifactId}' is unavailable, so publication '{publicationId}' cannot be unpublished.");

        var now = timeProvider.GetUtcNow();
        var deactivation = await activationCoordinator.DeactivateAsync(
            new WorkflowDeactivationCommand(executable, request.SlotName, PublicationActivator.Source, slot.Revision),
            cancellationToken);
        if (!deactivation.Succeeded)
        {
            if (deactivation.Outcome == WorkflowActivationOutcome.Conflict)
                throw new PublicationActivationException(ToFailure(deactivation.Conflict, deactivation.Diagnostic));

            // A compensated retraction failure leaves the publication exactly as it was — slot, projections and
            // record all still Active — so it is reported as a failed command, not as a partial unpublish.
            throw new InvalidOperationException(deactivation.CompensationDiagnostic is null
                ? $"Publication '{publicationId}' could not be unpublished; its slot authority and serving projections were restored. {deactivation.Diagnostic}"
                : $"Publication '{publicationId}' could not be unpublished and compensation failed: {deactivation.Diagnostic}");
        }

        var retired = publication with { Status = PublicationStatus.Retired, RetiredAt = now };
        if (!await publicationStore.TryTransitionAsync(retired, PublicationStatus.Active, cancellationToken))
            throw new InvalidOperationException($"Publication '{publicationId}' could not be retired.");
        if (publication.SourceReferenceId is { } sourceReferenceId)
        {
            logger?.LogInformation(
                "Unpublish: retiring source reference {SourceReferenceId} of workflow definition {DefinitionId} because publication {PublicationId} was unpublished from slot {SlotName}",
                sourceReferenceId,
                publication.WorkflowDefinitionId,
                publicationId,
                request.SlotName);
            await sourceReferenceStore.RetireAsync(sourceReferenceId, now, "publication-unpublished", cancellationToken);
        }
        return deactivation.Slot;
    }

    private static bool IsOwnedByPublishing(WorkflowActivationSlot slot) =>
        slot.Source is { } source && source.IsSameOwnerAs(PublicationActivator.Source);

    /// <summary>
    /// The ownership refusal an unpublish gets when the live activation was not put there by publishing. It shares
    /// <c>slot_owner_conflict</c> with <see cref="ToFailure"/>'s <see cref="WorkflowActivationConflict.ForeignSource"/>
    /// mapping because it is the same answer, reached one step earlier — and it names the owner, so an operator is
    /// told who holds the definition rather than that a record is missing.
    /// </summary>
    private static PublicationFailure ForeignActivationFailure(WorkflowActivationSlot slot, string activationId) => new(
        "slot_owner_conflict",
        $"Activation '{activationId}' of definition '{slot.WorkflowDefinitionId}' slot '{slot.SlotName}' was not published by " +
        $"'{PublicationActivator.Source.Describe()}'; it is owned by activation source '{slot.Source?.Describe() ?? "unknown"}' " +
        "and can only be withdrawn through that source.");

    internal static PublicationFailure ToFailure(WorkflowActivationConflict conflict, string? diagnostic) => conflict switch
    {
        WorkflowActivationConflict.ForeignSource =>
            new("slot_owner_conflict", diagnostic ?? "The activation slot is owned by another activation source."),
        _ => new("slot_revision_conflict", diagnostic ?? "The publication slot revision changed.")
    };
}

public sealed class RestorePublicationSlotRequestHandler(
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore publicationStore,
    IPublicationActivator activator,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider,
    ILogger<RestorePublicationSlotRequestHandler>? logger = null) : IRequestHandler<RestorePublicationSlot, WorkflowActivationSlot>
{
    public async Task<WorkflowActivationSlot> Handle(RestorePublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await activationAuthority.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' does not exist.");
        if (slot.ActiveActivationId is not null)
            return slot;

        var prior = (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .Where(publication => publication.Status == PublicationStatus.Retired)
            .OrderByDescending(publication => publication.RetiredAt ?? publication.CreatedAt)
            .ThenByDescending(publication => publication.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Publication slot '{request.SlotName}' has no retired publication to restore.");
        var executable = await executableStore.FindAsync(prior.ArtifactId, cancellationToken)
            ?? throw new InvalidOperationException($"Executable artifact '{prior.ArtifactId}' is unavailable for restore.");

        var priorReference = prior.SourceReferenceId is { } priorReferenceId
            ? await sourceReferenceStore.FindAsync(priorReferenceId, cancellationToken)
            : null;
        var now = timeProvider.GetUtcNow();
        var publicationId = $"publication-{ShortIdentityGenerator.Generate(now)}";
        // The coordinator owns activation↔reference identity; mint the same id here so the publication record
        // points at the reference that will actually be stored.
        var sourceReferenceId = WorkflowActivationReferenceIdentity.Create(publicationId);
        var candidate = prior with
        {
            PublicationId = publicationId,
            SlotId = slot.SlotId,
            SourceReferenceId = sourceReferenceId,
            ExpectedSlotRevision = slot.Revision,
            Status = PublicationStatus.Candidate,
            CreatedAt = now,
            ActivatedAt = null,
            RetiredAt = null,
            Failure = null
        };
        var reference = priorReference is null
            ? throw new InvalidOperationException($"Publication '{prior.PublicationId}' has no source reference to restore.")
            : priorReference with
            {
                SourceReferenceId = sourceReferenceId,
                CreatedAt = now,
                PublishedAt = now,
                DeletedAt = null,
                DeletedReason = null,
                ActivationId = publicationId,
                SlotId = slot.SlotId
            };

        // Lease, reference save, projections and compensation all belong to IWorkflowActivationCoordinator, which
        // the activator calls. Restore requests activation; it does not implement it.
        var activation = await activator.ActivateAsync(
            new PublicationActivationRequest(candidate, executable, reference),
            cancellationToken);
        if (!activation.Succeeded)
        {
            logger?.LogWarning(
                "Restore: activation of publication {PublicationId} for workflow definition {DefinitionId} failed with '{FailureCode}'",
                publicationId,
                candidate.WorkflowDefinitionId,
                activation.Failure?.Code);
            throw new PublicationActivationException(activation.Failure);
        }

        return activation.Slot;
    }
}
