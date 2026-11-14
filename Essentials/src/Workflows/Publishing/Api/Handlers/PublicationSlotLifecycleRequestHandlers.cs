using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>Retracts a publication through the runtime-owned activation lifecycle.</summary>
public interface IPublicationSlotUnpublisher
{
    Task<WorkflowActivationSlot> UnpublishAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken);
}

/// <summary>Reactivates a retired publication through the runtime-owned activation lifecycle.</summary>
public interface IPublicationSlotRestorer
{
    Task<WorkflowActivationSlot> RestoreAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken);
}

/// <summary>
/// Publishing bookkeeping around one runtime coordinator deactivation. Slot authority, projection removal,
/// observer notification and compensation belong exclusively to <see cref="IWorkflowActivationCoordinator"/>.
/// </summary>
public sealed class UnpublishPublicationSlotRequestHandler(
    IWorkflowActivationAuthority activationAuthority,
    IWorkflowActivationCoordinator activationCoordinator,
    IPublicationRecordStore publicationStore,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider,
    ILogger<UnpublishPublicationSlotRequestHandler>? logger = null) : IPublicationSlotUnpublisher, IRequestHandler<UnpublishPublicationSlot, WorkflowActivationSlot>
{
    public Task<WorkflowActivationSlot> UnpublishAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken) =>
        Handle(new UnpublishPublicationSlot(workflowDefinitionId, slotName), cancellationToken);

    public async Task<WorkflowActivationSlot> Handle(UnpublishPublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await activationAuthority.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)
            ?? throw new PublicationSlotNotFoundException($"Publication slot '{request.SlotName}' does not exist.");
        if (slot.ActiveActivationId is not { } publicationId)
            return slot;

        // Publishing can retract only a slot it owns. An activation inserted by another runtime source may have no
        // PublicationRecord at all; that is an ownership refusal, not missing publishing data.
        var publication = IsOwnedByPublishing(slot)
            ? await publicationStore.FindAsync(publicationId, cancellationToken)
            : null;
        if (publication is null)
            throw new PublicationActivationException(ForeignActivationFailure(slot, publicationId));

        var material = await PublicationSlotMaterialReader.FindAsync(executableStore, publication.ArtifactId, cancellationToken);
        var executable = material is not null ? material.NativeWorkflow
            : await executableStore.FindAsync(publication.ArtifactId, cancellationToken);
        if (material is null && executable is null)
            throw new PublicationSlotNotFoundException($"Executable artifact '{publication.ArtifactId}' is unavailable, so publication '{publicationId}' cannot be unpublished.");
        var sourceReference = publication.SourceReferenceId is { } referenceId
            ? await sourceReferenceStore.FindAsync(referenceId, cancellationToken) : null;
        // Native material can have a distinct logical definition, and its protected reference must keep the
        // material lifecycle even when producer and logical identities happen to match. Preserve legacy native
        // activation for existing unprotected publications and native-only coordinator replacements.
        var usesMaterialLifecycle = material?.Model is not null ||
            (material?.Identity.DefinitionId ?? executable!.Identity.DefinitionId) != slot.WorkflowDefinitionId || sourceReference?.PublicationScope is not null;
        if (usesMaterialLifecycle && material is null)
            throw new NotSupportedException("The selected artifact provider cannot read exact material for this logical publication lifecycle.");
        if (usesMaterialLifecycle && (publication.WorkflowDefinitionId != slot.WorkflowDefinitionId ||
            publication.SlotId != slot.SlotId || publication.SlotName != slot.SlotName ||
            publication.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(publicationId)))
            throw new PublicationSlotNotFoundException("The material publication does not belong to the selected logical slot and source.");
        var deactivation = usesMaterialLifecycle
            ? await activationCoordinator.DeactivateMaterialAsync(new(slot.WorkflowDefinitionId, material!, request.SlotName,
                PublicationActivator.Source, slot.Revision), cancellationToken)
            : await activationCoordinator.DeactivateAsync(new WorkflowDeactivationCommand(executable!, request.SlotName,
                PublicationActivator.Source, slot.Revision), cancellationToken);
        if (!deactivation.Succeeded)
        {
            if (deactivation.Outcome == WorkflowActivationOutcome.Conflict)
                throw new PublicationActivationException(ToFailure(deactivation.Conflict, deactivation.Diagnostic));

            throw new InvalidOperationException(deactivation.CompensationDiagnostic is null
                ? $"Publication '{publicationId}' could not be unpublished; its slot authority and serving projections were restored. {deactivation.Diagnostic}"
                : $"Publication '{publicationId}' could not be unpublished and compensation failed: {deactivation.Diagnostic}");
        }

        var now = timeProvider.GetUtcNow();
        // The record may still be a candidate, if a process stopped after the runtime's switch and nothing has completed
        // it since (#2223), and the publication it replaced may still be active. Only a sibling the runtime has recorded
        // as replaced is retired: a publish that won the slot after the deactivation owns a live reference and stays.
        await PublicationRecordRetirement.RetireAsync(publicationStore, publication, now, cancellationToken);
        await PublicationRecordRetirement.RetireReplacedAsync(publicationStore, sourceReferenceStore, slot.SlotId, publicationId, now, cancellationToken);
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

    private static PublicationFailure ForeignActivationFailure(WorkflowActivationSlot slot, string activationId) => new(
        PublicationFailureCodes.SlotOwnerConflict,
        $"Activation '{activationId}' of definition '{slot.WorkflowDefinitionId}' slot '{slot.SlotName}' was not published by " +
        $"'{PublicationActivator.Source.Describe()}'; it is owned by activation source '{slot.Source?.Describe() ?? "unknown"}' " +
        "and can only be withdrawn through that source.");

    internal static PublicationFailure ToFailure(WorkflowActivationConflict conflict, string? diagnostic) => conflict switch
    {
        WorkflowActivationConflict.ForeignSource =>
            new(PublicationFailureCodes.SlotOwnerConflict, diagnostic ?? "The activation slot is owned by another activation source."),
        _ => new(PublicationFailureCodes.SlotRevisionConflict, diagnostic ?? "The publication slot revision changed.")
    };
}

/// <summary>Restores publishing's most recently retired record through one runtime activation call.</summary>
public sealed class RestorePublicationSlotRequestHandler(
    IWorkflowActivationAuthority activationAuthority,
    IPublicationRecordStore publicationStore,
    IPublicationActivator activator,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider,
    ILogger<RestorePublicationSlotRequestHandler>? logger = null) : IPublicationSlotRestorer, IRequestHandler<RestorePublicationSlot, WorkflowActivationSlot>
{
    public Task<WorkflowActivationSlot> RestoreAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken) =>
        Handle(new RestorePublicationSlot(workflowDefinitionId, slotName), cancellationToken);

    public async Task<WorkflowActivationSlot> Handle(RestorePublicationSlot request, CancellationToken cancellationToken)
    {
        var slot = await activationAuthority.FindAsync(request.WorkflowDefinitionId, request.SlotName, cancellationToken)
            ?? throw new PublicationSlotNotFoundException($"Publication slot '{request.SlotName}' does not exist.");
        if (slot.ActiveActivationId is not null)
            return slot;

        var prior = (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
            .Where(publication => publication.Status == PublicationStatus.Retired)
            .OrderByDescending(publication => publication.RetiredAt ?? publication.CreatedAt)
            .ThenByDescending(publication => publication.PublicationId, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new PublicationSlotNotFoundException($"Publication slot '{request.SlotName}' has no retired publication to restore.");
        if (prior.WorkflowDefinitionId != slot.WorkflowDefinitionId || prior.SlotId != slot.SlotId || prior.SlotName != slot.SlotName)
            throw new PublicationSlotNotFoundException("The retired publication does not belong to the selected logical slot.");
        var material = await PublicationSlotMaterialReader.FindAsync(executableStore, prior.ArtifactId, cancellationToken);
        var executable = material is not null ? material.NativeWorkflow
            : await executableStore.FindAsync(prior.ArtifactId, cancellationToken);
        if (material is null && executable is null)
            throw new PublicationSlotNotFoundException($"Executable artifact '{prior.ArtifactId}' is unavailable for restore.");

        var priorReference = prior.SourceReferenceId is { } priorReferenceId
            ? await sourceReferenceStore.FindAsync(priorReferenceId, cancellationToken)
            : null;
        var usesMaterialLifecycle = material?.Model is not null ||
            (material?.Identity.DefinitionId ?? executable!.Identity.DefinitionId) != slot.WorkflowDefinitionId || priorReference?.PublicationScope is not null;
        if (usesMaterialLifecycle && material is null)
            throw new NotSupportedException("The selected artifact provider cannot read exact material for this logical publication lifecycle.");
        if (usesMaterialLifecycle && (priorReference is null || priorReference.ArtifactId != material!.Identity.ArtifactId ||
            priorReference.DefinitionId != prior.WorkflowDefinitionId || priorReference.DefinitionVersionId != prior.WorkflowDefinitionVersionId ||
            priorReference.Scope != WorkflowExecutableReferenceScope.Published || priorReference.ActivationId != prior.PublicationId ||
            priorReference.SlotId != slot.SlotId || priorReference.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(prior.PublicationId)))
            throw new PublicationSlotNotFoundException("The retired material publication lacks its exact logical source provenance.");
        var now = timeProvider.GetUtcNow();
        var publicationId = $"publication-{ShortIdentityGenerator.Generate(now)}";
        var sourceReferenceId = WorkflowActivationReferenceIdentity.Create(publicationId);
        var candidate = prior with
        {
            PublicationId = publicationId,
            SourceReferenceId = sourceReferenceId,
            RequestFingerprint = null,
            ExpectedSlotRevision = slot.Revision,
            Status = PublicationStatus.Candidate,
            CreatedAt = now,
            ActivatedAt = null,
            RetiredAt = null,
            Failure = null
        };
        var reference = priorReference is null
            ? throw new PublicationSlotNotFoundException($"Publication '{prior.PublicationId}' has no source reference to restore.")
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

        var activation = usesMaterialLifecycle
            ? await activator.ActivateMaterialAsync(new(candidate, material!, reference), cancellationToken)
            : await activator.ActivateAsync(new PublicationActivationRequest(candidate, executable!, reference), cancellationToken);
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

// SupportsModelMaterial qualifies model support, not the independent exact native-material read seam.
internal static class PublicationSlotMaterialReader
{
    internal static async ValueTask<RuntimeExecutionMaterial?> FindAsync(IWorkflowExecutableStore store, string artifactId, CancellationToken cancellationToken)
    {
        try { return await store.FindMaterialAsync(artifactId, cancellationToken); }
        catch (NotSupportedException) when (!store.SupportsModelMaterial)
        {
            // Genuine legacy native stores retain their ordinary path. Callers still refuse any protected or
            // distinct-logical publication before side effects when the exact material seam is unavailable.
            return null;
        }
    }
}
