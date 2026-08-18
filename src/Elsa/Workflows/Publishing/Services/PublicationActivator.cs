using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Publishing's <see cref="PublicationRecord"/> bookkeeping wrapped around one call to the shared
/// <see cref="IWorkflowActivationCoordinator"/>.
/// </summary>
/// <remarks>
/// <para>
/// It holds <b>no</b> copy of the activation sequence (FR-B-006): leases, source references, serving projections,
/// the slot CAS, observer notification and compensation all belong to the coordinator. What stays here is the
/// publication journal — save the candidate, record the outcome, retire the record the transition displaced.
/// </para>
/// <para>
/// The journal is a record of requests publishing made to the authority, never serving truth. A journal write that
/// fails <i>after</i> the coordinator reported success therefore does not un-activate anything <b>and does not make
/// the publish a failure</b>: the slot is the authority, the workflow is live, and any Status/slot divergence
/// resolves in favour of the slot (FR-B-006). Such a write failure is logged as an operational incident.
/// </para>
/// </remarks>
public sealed class PublicationActivator(
    IWorkflowActivationCoordinator activationCoordinator,
    IPublicationRecordStore publicationStore,
    TimeProvider timeProvider,
    ILogger<PublicationActivator>? logger = null) : IPublicationActivator
{
    /// <summary>The activation source every publish-pipeline request is owned by.</summary>
    public static WorkflowActivationSource Source => WorkflowActivationSource.Publishing;

    /// <summary>
    /// Every publish is an explicit operator command, so it claims the slot even when another activation source
    /// owns the definition (T118, amending FR-B-006).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is where "explicit wins" lives — and it lives here on purpose.</b> The runtime honours the intent
    /// generically and never learns that publishing exists; what makes publishing outrank a boot-time declarative
    /// import is that publishing, and only publishing, declares the intent. A human ran a publish command, and
    /// this is the only layer that knows it.
    /// </para>
    /// <para>
    /// The asymmetry's other half is the artifact reconciler's, not this type's: it never declares the intent, so
    /// a later shell reload cannot quietly revert the operator's publish. Handing control back to a mount is an
    /// unpublish, which clears ownership and makes the slot claimable again.
    /// </para>
    /// </remarks>
    public const WorkflowActivationOwnershipIntent OwnershipIntent = WorkflowActivationOwnershipIntent.TakeOver;

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
            activation = await activationCoordinator.ActivateAsync(
                new WorkflowActivationCommand(
                    request.Executable,
                    request.Reference,
                    candidate.SlotName,
                    candidate.PublicationId,
                    Source,
                    candidate.ExpectedSlotRevision,
                    OwnershipIntent),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkflowActivationException exception)
        {
            // The coordinator refused to attempt the sequence at all (uncomposed trigger spine, unavailable
            // lease). Nothing was written on its side, so the journal converges and the domain exception — which
            // carries the original infrastructure fault as its inner exception — keeps travelling.
            await FailCandidateAsync(
                candidate,
                new PublicationFailure("publication_activation_refused", SafeMessage(exception.Message)),
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
        var active = candidate with
        {
            Status = PublicationStatus.Active,
            ActivatedAt = now,
            RetiredAt = null,
            Failure = null
        };
        try
        {
            await TransitionOrThrowAsync(active, PublicationStatus.Candidate, cancellationToken);
            await RetireReplacedRecordAsync(candidate, activation.ReplacedActivationId, now, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // FR-B-006, approved 2026-08-16. The slot has already flipped, so the candidate IS serving. Only the
            // JOURNAL write failed — the record of the activation, never the activation itself. Rolling back here
            // (or reporting failure, which invites the caller to roll back) would make journal availability a
            // dependency of serving, which FR-B-006 forbids: "Status ... MUST NOT be consulted to decide serving;
            // divergence resolves in favour of the slot." So the outcome is reported truthfully as success and the
            // divergence is raised as an operational incident instead.
            logger?.LogError(
                exception,
                "Publication {PublicationId} of workflow definition {DefinitionId} slot {SlotName} IS ACTIVE — the activation slot flipped and its serving projections are live — but the publication journal could not be updated to match. "
                + "Serving is unaffected and MUST NOT be rolled back; the journal now diverges from the slot and needs operator reconciliation.",
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

    /// <summary>
    /// Maps a coordinator refusal onto publishing's failure vocabulary. Every code below is the one publishing
    /// reported for the same condition before the activation sequence moved into the coordinator — a caller of
    /// the publish API must not be able to tell that the sequence changed owners.
    /// </summary>
    /// <remarks>
    /// Order matters. A slot refusal is reported as the conflict it is even if the rollback afterwards did not
    /// converge, matching publishing's original behavior: compensation ran only for post-flip failures, so
    /// <c>activation_compensation_failed</c> could never mask a revision conflict.
    /// </remarks>
    private static PublicationFailure MapFailure(WorkflowActivationResult activation) => activation.Conflict switch
    {
        WorkflowActivationConflict.RevisionMismatch =>
            new("slot_revision_conflict", activation.Diagnostic ?? "The publication slot revision changed."),
        WorkflowActivationConflict.ForeignSource =>
            new("slot_owner_conflict", activation.Diagnostic ?? "The activation slot is owned by another activation source."),
        _ when activation.CompensationDiagnostic is not null =>
            new("activation_compensation_failed", activation.Diagnostic ?? "Publication activation failed and its compensation did not converge."),
        _ => new(MapFailedStep(activation.FailedStep), activation.Diagnostic ?? "Publication activation failed.")
    };

    /// <summary>
    /// Publishing's incident code per activation step. The coordinator reports <i>which</i> step broke; publishing
    /// owns what that means to its own callers, so the mapping lives here rather than in the runtime.
    /// </summary>
    private static string MapFailedStep(WorkflowActivationStep step) => step switch
    {
        WorkflowActivationStep.ProjectionPreparation => "projection_preparation_failed",
        // Observer notification is part of making the projections serve — it was inside the projection preparer's
        // Activate before the extraction, and shares its code so the incident vocabulary is unchanged.
        WorkflowActivationStep.ProjectionActivation or WorkflowActivationStep.TriggerObserverNotification =>
            "projection_activation_failed",
        _ => "publication_activation_failed"
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

        // A missing record is an ANSWER, not a fault: the displaced activation was not published by us. Before
        // T118 that could only happen on a corrupted journal, so throwing was defensible; a takeover displaces a
        // foreign source's activation as a matter of course, and there is nothing of publishing's to retire.
        // (The runtime still retires the displaced activation's source reference — that is the coordinator's job,
        // and it happens for every predecessor regardless of who owned it.)
        var replaced = await publicationStore.FindAsync(replacedId, cancellationToken);
        if (replaced is null)
        {
            logger?.LogDebug(
                "Publication {PublicationId} displaced activation {ReplacedActivationId}, which has no publication record: it was activated by another source and there is nothing to retire in this journal",
                candidate.PublicationId,
                replacedId);
            return;
        }

        var retired = replaced with { Status = PublicationStatus.Retired, RetiredAt = now };
        await TransitionOrThrowAsync(retired, PublicationStatus.Active, cancellationToken);
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
        message.Length <= 512 ? message : message[..512];

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
