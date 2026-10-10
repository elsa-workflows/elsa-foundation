using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Publishing's <see cref="PublicationRecord"/> bookkeeping wrapped around one call to the shared
/// <see cref="IWorkflowActivationCoordinator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Leases, source references, serving projections, slot CAS, observer notification and compensation all belong to
/// the runtime coordinator. This type owns only the publication journal and maps runtime outcomes to publishing's
/// failure vocabulary.
/// </para>
/// <para>
/// The journal follows the slot; it never decides serving. It is written after the runtime's commit
/// (<see cref="IWorkflowActivationSwitch"/>), so a process that stops in between leaves it lagging the slot (#2223), and
/// <see cref="CompleteAsync"/> brings it back into line: before every activation, on a same-version republish that finds
/// the journal lagging, and at shell start (<see cref="CompleteInterruptedPublicationsStartupTask"/>). It reads the slot
/// and the source references, which the runtime owns, and writes only publication records.
/// </para>
/// </remarks>
public sealed class PublicationActivator(
    IWorkflowActivationCoordinator activationCoordinator,
    IPublicationRecordStore publicationStore,
    IWorkflowActivationAuthority activationAuthority,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    TimeProvider timeProvider,
    ILogger<PublicationActivator>? logger = null) : IPublicationActivator
{
    /// <summary>The activation source every publish-pipeline request is owned by.</summary>
    public static WorkflowActivationSource Source => WorkflowActivationSource.Publishing;

    public async ValueTask<PublicationActivationResult> ActivateAsync(
        PublicationActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Candidate);
        ArgumentNullException.ThrowIfNull(request.Executable);
        ArgumentNullException.ThrowIfNull(request.Reference);
        var candidate = request.Candidate;
        ValidateCandidate(candidate);

        await publicationStore.SaveAsync(candidate, cancellationToken);

        WorkflowActivationResult activation;
        try
        {
            // Bring the publication the slot names into line before replacing it (#2223): the runtime makes sure its
            // activation serves, and the journal, which may still hold it as a candidate, is updated, because retiring a
            // candidate as the replaced publication would fail.
            var completion = await CompleteAsync(candidate.WorkflowDefinitionId, candidate.SlotName, cancellationToken);
            if (!completion.Succeeded)
                return new PublicationActivationResult(
                    false,
                    await FailCandidateAsync(candidate, completion.Failure!, cancellationToken),
                    completion.Slot,
                    completion.Failure);

            activation = await activationCoordinator.ActivateAsync(
                new WorkflowActivationCommand(
                    request.Executable,
                    request.Reference,
                    candidate.SlotName,
                    candidate.PublicationId,
                    Source,
                    candidate.ExpectedSlotRevision),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkflowActivationException exception)
        {
            // The coordinator refused to run the lifecycle. Its own writes did not run, so the journal candidate can
            // still be marked failed while the coordinator's domain exception continues to the caller.
            await FailCandidateAsync(
                candidate,
                new PublicationFailure(PublicationFailureCodes.PublicationActivationRefused, SafeMessage(exception.Message)),
                CancellationToken.None);
            throw;
        }

        if (!activation.Succeeded)
        {
            var failure = MapFailure(activation);
            var failed = await FailCandidateAsync(candidate, failure, cancellationToken);
            return new PublicationActivationResult(
                false,
                failed,
                activation.Slot,
                failure,
                activation.ReplacedActivationId);
        }

        // The coordinator answers AlreadyActive without minting anything when the slot already serves this artifact, and the
        // activation it names may be another publication's. Marking the candidate active then would journal a second
        // active record for a reference that was never minted, and nothing would ever retire it.
        if (activation.Outcome == WorkflowActivationOutcome.AlreadyActive &&
            !StringComparer.Ordinal.Equals(activation.Slot.ActiveActivationId, candidate.PublicationId))
            return await ResolveServedByAnotherPublicationAsync(candidate, activation.Slot, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var active = Activated(candidate, now);
        try
        {
            // The candidate's own transition is the last journal write, so a process that stops before it leaves the
            // candidate lagging the slot, which is what CompleteAsync looks for. A completion of this slot on another
            // node may have recorded it first, and a replacement that followed may have retired it already; both leave
            // the journal right.
            await RetireReplacedRecordAsync(candidate, activation.ReplacedActivationId, now, cancellationToken);
            var recorded = await MarkActiveAsync(candidate, now, cancellationToken);
            if (recorded.Status is not (PublicationStatus.Active or PublicationStatus.Retired))
                throw new InvalidOperationException(
                    $"Publication '{candidate.PublicationId}' did not transition from 'Candidate' to 'Active'; it is '{recorded.Status}'.");
            active = recorded;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The slot has already flipped and serving projections are live. A journal failure must not roll back
            // serving; the slot is runtime authority, and CompleteAsync brings the journal back into line.
            logger?.LogError(
                exception,
                "Publication {PublicationId} of workflow definition {DefinitionId} slot {SlotName} is active, but its publication journal could not be updated to match.",
                candidate.PublicationId,
                candidate.WorkflowDefinitionId,
                candidate.SlotName);
        }

        return new PublicationActivationResult(
            true,
            active,
            activation.Slot,
            ReplacedPublicationId: activation.ReplacedActivationId);
    }

    public async ValueTask<PublicationCompletionResult> CompleteAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slotName);

        // The runtime first: the journal may say a publication serves only once its activation does.
        var completion = await activationCoordinator.EnsureServingAsync(workflowDefinitionId, slotName, cancellationToken);
        var slot = completion.Slot;
        if (!completion.Succeeded)
            return new(false, slot, Failure: MapFailure(completion));
        if (slot is not { ActiveActivationId: { } publicationId, Source: { } source } ||
            !source.IsSameOwnerAs(Source) ||
            await publicationStore.FindAsync(publicationId, cancellationToken) is not { } publication)
            return new(true, slot);

        var now = timeProvider.GetUtcNow();

        // A publication the slot names that is already active is the common case and costs one read. One still a
        // candidate, or retired while the slot names it again, lags the slot. Marking it active is the last write, so a
        // process that stops before it leaves it lagging for the next completion.
        if (publication.Status is not (PublicationStatus.Candidate or PublicationStatus.Retired) ||
            !await ServesAsync(slot, publication, cancellationToken))
            return new(true, slot, publication);

        var retired = await PublicationRecordRetirement.RetireReplacedAsync(publicationStore, sourceReferenceStore, slot.SlotId, publicationId, now, cancellationToken);
        var lagged = publication.Status;
        publication = await MarkActiveAsync(publication, now, cancellationToken);
        if (lagged == PublicationStatus.Retired && publication.Status == PublicationStatus.Active)
            publication = await RetireIfSlotMovedAsync(slot, publication, now, cancellationToken);
        logger?.LogWarning(
            "The publication journal of definition {DefinitionId} slot {SlotName} lagged the slot, which an interrupted call left half done: publication {PublicationId} was {LaggedStatus} and is now {Status}, and replaced publications {RetiredPublicationIds} are retired",
            workflowDefinitionId,
            slotName,
            publicationId,
            lagged,
            publication.Status,
            retired);
        return new(true, slot, publication);
    }

    /// <summary>
    /// The slot already serves the candidate's artifact through another activation, which a same-version publish that won a
    /// race, or one that read the slot before it moved, leaves behind. The candidate never serves, so it is recorded as
    /// failed and no second active record exists. The caller asked for the artifact to serve and it does: when the
    /// publication the slot names is active once the journal is brought into line with it, that record is the answer,
    /// as it is for a same-version republish the handler recognises up front. Otherwise nothing proves the artifact
    /// serves under a publication of ours, and the request fails.
    /// </summary>
    private async ValueTask<PublicationActivationResult> ResolveServedByAnotherPublicationAsync(
        PublicationRecord candidate,
        WorkflowActivationSlot served,
        CancellationToken cancellationToken)
    {
        var servedId = served.ActiveActivationId!;
        PublicationFailure failure;
        if (served.Source is { } owner && !owner.IsSameOwnerAs(Source))
            failure = new(
                PublicationFailureCodes.SlotOwnerConflict,
                $"Definition '{candidate.WorkflowDefinitionId}' slot '{candidate.SlotName}' is owned by activation source '{owner.Describe()}'; " +
                $"'{Source.Describe()}' cannot publish to it. Ownership transfer is an explicit operator action.");
        else
        {
            var completion = await CompleteAsync(candidate.WorkflowDefinitionId, candidate.SlotName, cancellationToken);
            if (completion is { Succeeded: true, Publication: { Status: PublicationStatus.Active } publication } &&
                StringComparer.Ordinal.Equals(publication.PublicationId, servedId))
            {
                await FailCandidateAsync(
                    candidate,
                    new(PublicationFailureCodes.ArtifactAlreadyServing, $"Publication '{servedId}' already serves artifact '{candidate.ArtifactId}' in this slot."),
                    cancellationToken);
                return new(true, publication, completion.Slot);
            }

            failure = completion.Failure ?? new(
                PublicationFailureCodes.PublicationActivationFailed,
                $"Definition '{candidate.WorkflowDefinitionId}' slot '{candidate.SlotName}' already serves artifact '{candidate.ArtifactId}' " +
                $"through '{servedId}', which is not an active publication of the slot: it moved on, or it does not serve. " +
                "The candidate was not activated.");
        }

        return new(false, await FailCandidateAsync(candidate, failure, cancellationToken), served, failure);
    }

    private static PublicationFailure MapFailure(WorkflowActivationResult activation) => activation.Conflict switch
    {
        WorkflowActivationConflict.RevisionMismatch =>
            new(PublicationFailureCodes.SlotRevisionConflict, activation.Diagnostic ?? "The publication slot revision changed."),
        WorkflowActivationConflict.ForeignSource =>
            new(PublicationFailureCodes.SlotOwnerConflict, activation.Diagnostic ?? "The activation slot is owned by another activation source."),
        _ when activation.CompensationDiagnostic is not null =>
            new(PublicationFailureCodes.ActivationCompensationFailed, activation.Diagnostic ?? "Publication activation failed and its compensation did not converge."),
        _ => new(MapFailedStep(activation.FailedStep), activation.Diagnostic ?? "Publication activation failed.")
    };

    private static string MapFailedStep(WorkflowActivationStep step) => step switch
    {
        WorkflowActivationStep.ProjectionPreparation => PublicationFailureCodes.ProjectionPreparationFailed,
        WorkflowActivationStep.ProjectionActivation or WorkflowActivationStep.TriggerObserverNotification =>
            PublicationFailureCodes.ProjectionActivationFailed,
        _ => PublicationFailureCodes.PublicationActivationFailed
    };

    private async ValueTask<PublicationRecord> FailCandidateAsync(
        PublicationRecord candidate,
        PublicationFailure failure,
        CancellationToken cancellationToken)
    {
        var current = await publicationStore.FindAsync(candidate.PublicationId, cancellationToken) ?? candidate;
        if (current.Status is not (PublicationStatus.Candidate or PublicationStatus.Active))
            return current;

        var failed = current with
        {
            Status = PublicationStatus.Failed,
            ActivatedAt = null,
            RetiredAt = null,
            Failure = failure
        };
        await TransitionOrThrowAsync(failed, current.Status, cancellationToken);
        return failed;
    }

    private async ValueTask RetireReplacedRecordAsync(
        PublicationRecord candidate,
        string? replacedPublicationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (replacedPublicationId is not { } replacedId ||
            StringComparer.Ordinal.Equals(replacedId, candidate.PublicationId))
            return;

        var replaced = await publicationStore.FindAsync(replacedId, cancellationToken)
            ?? throw new InvalidOperationException($"The replaced publication '{replacedId}' does not exist.");
        await PublicationRecordRetirement.RetireAsync(publicationStore, replaced, now, cancellationToken);
    }

    /// <summary>
    /// The slot still names <paramref name="publication"/> at the revision completion read, and its source reference is
    /// live. Completion has then switched its activation on, so it serves. A retired reference reads as nothing active,
    /// as it does to the coordinator: the activation failed and was compensated.
    /// </summary>
    private async ValueTask<bool> ServesAsync(WorkflowActivationSlot slot, PublicationRecord publication, CancellationToken cancellationToken) =>
        await FindReferenceAsync(publication, cancellationToken) is { DeletedAt: null } &&
        await activationAuthority.FindAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellationToken) is { } current &&
        current.Revision == slot.Revision &&
        StringComparer.Ordinal.Equals(current.ActiveActivationId, slot.ActiveActivationId);

    /// <summary>
    /// Takes back a retired publication that was just marked active when the slot no longer names it. A retired
    /// publication is nobody's replaced record: once the slot moves on, nothing retires it again, so the mark would
    /// otherwise stay. A publication that was a candidate needs no such check, because the activation that replaces it
    /// retires it through the slot's own transitions.
    /// </summary>
    private async ValueTask<PublicationRecord> RetireIfSlotMovedAsync(
        WorkflowActivationSlot slot,
        PublicationRecord publication,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await activationAuthority.FindAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellationToken) is { } current &&
            StringComparer.Ordinal.Equals(current.ActiveActivationId, publication.PublicationId))
            return publication;

        await PublicationRecordRetirement.RetireAsync(publicationStore, publication, now, cancellationToken);
        return await publicationStore.FindAsync(publication.PublicationId, cancellationToken) ?? publication;
    }

    private async ValueTask<WorkflowExecutableSourceReference?> FindReferenceAsync(PublicationRecord publication, CancellationToken cancellationToken) =>
        publication.SourceReferenceId is { } sourceReferenceId
            ? await sourceReferenceStore.FindAsync(sourceReferenceId, cancellationToken)
            : null;

    /// <summary>
    /// Moves <paramref name="publication"/> to <see cref="PublicationStatus.Active"/> from the status it was read in, and
    /// returns the record as the journal then holds it. When another writer moved it first, that is the record as that
    /// writer left it: a concurrent completion of the same slot makes the same move.
    /// </summary>
    private async ValueTask<PublicationRecord> MarkActiveAsync(PublicationRecord publication, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = Activated(publication, now);
        return await publicationStore.TryTransitionAsync(active, publication.Status, cancellationToken)
            ? active
            : await publicationStore.FindAsync(publication.PublicationId, cancellationToken)
              ?? throw new InvalidOperationException($"Publication '{publication.PublicationId}' disappeared while it was being activated.");
    }

    private static PublicationRecord Activated(PublicationRecord publication, DateTimeOffset now) => publication with
    {
        Status = PublicationStatus.Active,
        ActivatedAt = publication.ActivatedAt ?? now,
        RetiredAt = null,
        Failure = null
    };

    private async ValueTask TransitionOrThrowAsync(
        PublicationRecord publication,
        PublicationStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        if (!await publicationStore.TryTransitionAsync(publication, expectedStatus, cancellationToken))
            throw new InvalidOperationException(
                $"Publication '{publication.PublicationId}' did not transition from '{expectedStatus}' to '{publication.Status}'.");
    }

    private static string SafeMessage(string message) =>
        string.IsNullOrWhiteSpace(message)
            ? "Publication activation failed."
            : message.Length <= 512 ? message : message[..512];

    private static void ValidateCandidate(PublicationRecord candidate)
    {
        if (candidate.Status != PublicationStatus.Candidate)
            throw new ArgumentException("Publication activation requires a Candidate record.", nameof(candidate));
        if (!StringComparer.Ordinal.Equals(
                candidate.SlotId,
                WorkflowActivationSlotIdentity.Create(candidate.WorkflowDefinitionId, candidate.SlotName)))
            throw new ArgumentException("The publication slot identity does not match its definition and slot name.", nameof(candidate));
    }
}
