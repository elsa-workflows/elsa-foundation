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
/// The journal follows the slot; it never decides serving. The slot transition commits before the projections switch
/// and before the journal is written, so a process that stops in between leaves the slot's publication a candidate
/// and the one it replaced active (#2223). <see cref="CompleteAsync"/> brings the journal back into line once the
/// runtime has completed the slot's activation. It runs before every activation, on a same-version republish that
/// finds the journal lagging, and at shell start (<see cref="CompleteInterruptedPublicationsStartupTask"/>). It reads
/// the slot and the source references, which the runtime owns, and writes only publication records.
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
            // Complete the publication the slot names, and its journal, before replacing it (#2223). The coordinator
            // completes the activation itself, but the journal would still hold that publication as a candidate, and
            // retiring it as the replaced publication would fail.
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
        var completion = await activationCoordinator.CompleteAsync(workflowDefinitionId, slotName, cancellationToken);
        var slot = completion.Slot;
        if (!completion.Succeeded)
            return new(false, slot, Failure: MapFailure(completion));
        if (slot is not { ActiveActivationId: { } publicationId, Source: { } source } ||
            !source.IsSameOwnerAs(Source) ||
            await publicationStore.FindAsync(publicationId, cancellationToken) is not { } publication)
            return new(true, slot);

        // A publication the slot names that is already active is the common case and costs one read. One still a
        // candidate, or retired while the slot names it again, lags the slot. Marking it active is the last write, so a
        // process that stops before it leaves it lagging for the next completion.
        if (publication.Status is not (PublicationStatus.Candidate or PublicationStatus.Retired) ||
            !await ServesAsync(slot, publication, cancellationToken))
            return new(true, slot, publication);

        var now = timeProvider.GetUtcNow();
        var retired = await RetireReplacedRecordsAsync(slot, publicationId, now, cancellationToken);
        var lagged = publication.Status;
        publication = await MarkActiveAsync(publication, now, cancellationToken);
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
        await RetireAsync(replaced, now, cancellationToken);
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
    /// Retires every other active publication of <paramref name="slot"/> whose activation the runtime has recorded as
    /// replaced: its source reference is retired or gone. One whose reference is live is left alone, because nothing
    /// then says it stopped serving, and so is one whose reference a failed activation's compensation retired: that
    /// activation's own activator records it as failed.
    /// </summary>
    /// <returns>The publications this call retired.</returns>
    private async ValueTask<IReadOnlyList<string>> RetireReplacedRecordsAsync(
        WorkflowActivationSlot slot,
        string publicationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var retired = new List<string>();
        foreach (var other in (await publicationStore.ListBySlotAsync(slot.SlotId, cancellationToken))
                     .Where(other => other.Status == PublicationStatus.Active && !StringComparer.Ordinal.Equals(other.PublicationId, publicationId)))
        {
            var reference = await FindReferenceAsync(other, cancellationToken);
            if (reference is { DeletedAt: null } ||
                StringComparer.Ordinal.Equals(reference?.DeletedReason, WorkflowActivationCoordinator.FailedRetireReason))
                continue;
            await RetireAsync(other, now, cancellationToken);
            retired.Add(other.PublicationId);
        }

        return retired;
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

    /// <summary>
    /// Retires a publication the slot no longer names. A candidate is retired too, with an activation time: a process that
    /// stopped after its slot transition left it one, and it served once completion switched it on. A lost
    /// compare-and-swap is re-read once, because a concurrent completion may just have made the candidate active. A
    /// publication already retired or failed is left as it is.
    /// </summary>
    private async ValueTask RetireAsync(PublicationRecord? publication, DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; publication is { Status: PublicationStatus.Candidate or PublicationStatus.Active }; attempt++)
        {
            if (attempt == 2)
                throw new InvalidOperationException(
                    $"Publication '{publication.PublicationId}' did not transition from '{publication.Status}' to 'Retired'.");
            var retired = publication with
            {
                Status = PublicationStatus.Retired,
                ActivatedAt = publication.ActivatedAt ?? now,
                RetiredAt = now
            };
            if (await publicationStore.TryTransitionAsync(retired, publication.Status, cancellationToken))
                return;
            publication = await publicationStore.FindAsync(publication.PublicationId, cancellationToken);
        }
    }

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
