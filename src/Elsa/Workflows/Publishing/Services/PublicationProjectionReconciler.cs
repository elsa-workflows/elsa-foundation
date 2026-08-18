using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Persists and idempotently delivers publication-scoped serving-projection intents.
/// </summary>
public sealed class PublicationProjectionReconciler(
    IPublicationProjectionIntentStore intentStore,
    IWorkflowExecutableStore executableStore,
    IWorkflowTriggerIndexer triggerIndexer,
    IWorkflowTriggerBindingStore triggerBindingStore,
    TimeProvider timeProvider,
    IRecurringTriggerScheduleStore? recurringScheduleStore = null,
    IEnumerable<IWorkflowTriggerIndexObserver>? triggerObservers = null,
    IRecurringTriggerScheduleProjectionPreparer? recurringSchedulePreparer = null) : IPublicationProjectionPreparer
{
    private readonly IReadOnlyCollection<IWorkflowTriggerIndexObserver> _triggerObservers = triggerObservers?.ToArray() ?? [];

    public ValueTask PrepareAsync(PublicationRecord candidate, CancellationToken cancellationToken = default) =>
        PrepareAsync(candidate, forceReplay: false, cancellationToken);

    private async ValueTask PrepareAsync(
        PublicationRecord candidate,
        bool forceReplay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var executable = await executableStore.FindAsync(candidate.ArtifactId, cancellationToken)
            ?? throw new InvalidOperationException($"Executable artifact '{candidate.ArtifactId}' was not found for publication '{candidate.PublicationId}'.");

        // ONE owned write per projection (FR-B-006 writer census, finding 3). Both projections are prepared under
        // the SAME delivery record, deliberately: the read-back-then-re-prepare that used to follow under a second
        // record wrote the schedule projection twice, and its independent record could short-circuit (see
        // DeliverAsync's Delivered guard) out of step with the write that actually produced the projection — a
        // replay whose bindings record was already Delivered skipped the indexer, then re-prepared the schedules
        // from an empty read-back and erased them. One write under one record removes both hazards.
        //
        // The recurring schedule is prepared explicitly here, in the same order the activation coordinator uses
        // (recurrences materialized and validated before any binding is written). It used to arrive implicitly
        // through a decorator over IWorkflowTriggerIndexer; T044b retired that decorator because it made the
        // indexer a replacement contract that silently owned a second obligation. Preparing it explicitly is the
        // point of that change — and omitting it here would leave ActivateAsync below activating a recurring
        // projection that was never prepared, which on the RestoreAsync compensation path means a workflow's
        // timers silently stop after a failed unpublish.
        await DeliverAsync(
            candidate.PublicationId,
            PublicationProjectionKinds.TriggerBindings,
            PublicationProjectionOperation.Prepare,
            async ct =>
            {
                if (recurringSchedulePreparer is not null)
                    await recurringSchedulePreparer.PrepareActivationAsync(executable, candidate.PublicationId, candidate.SlotId, ct);
                await triggerIndexer.PrepareActivationAsync(executable, candidate.PublicationId, candidate.SlotId, ct);
            },
            cancellationToken,
            forceReplay);
    }

    public ValueTask ActivateAsync(
        PublicationRecord candidate,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default) =>
        ActivateAsync(candidate, replacedPublicationId, forceReplay: false, cancellationToken);

    private async ValueTask ActivateAsync(
        PublicationRecord candidate,
        string? replacedPublicationId,
        bool forceReplay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        await DeliverAsync(
            candidate.PublicationId,
            PublicationProjectionKinds.TriggerBindings,
            PublicationProjectionOperation.Activate,
            ct => triggerBindingStore.ActivateAsync(candidate.PublicationId, replacedPublicationId, ct),
            cancellationToken,
            forceReplay);

        if (recurringScheduleStore is not null)
            await DeliverAsync(
                candidate.PublicationId,
                PublicationProjectionKinds.RecurringSchedules,
                PublicationProjectionOperation.Activate,
                ct => recurringScheduleStore.ActivateAsync(candidate.PublicationId, replacedPublicationId, ct),
                cancellationToken,
                forceReplay);

        await NotifyTriggerObserversAsync(candidate, cancellationToken);
    }

    public async ValueTask CompensateAsync(
        PublicationRecord candidate,
        string? restoredPublicationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (restoredPublicationId is not null)
        {
            await DeliverAsync(
                restoredPublicationId,
                PublicationProjectionKinds.TriggerBindings,
                PublicationProjectionOperation.Activate,
                ct => triggerBindingStore.ActivateAsync(restoredPublicationId, candidate.PublicationId, ct),
                cancellationToken,
                forceReplay: true);

            if (recurringScheduleStore is not null)
                await DeliverAsync(
                    restoredPublicationId,
                    PublicationProjectionKinds.RecurringSchedules,
                    PublicationProjectionOperation.Activate,
                    ct => recurringScheduleStore.ActivateAsync(restoredPublicationId, candidate.PublicationId, ct),
                    cancellationToken,
                    forceReplay: true);
        }

        // Reconcile observers only after both sides of compensation have reached their final serving state. A
        // refresh between restoring the previous publication and removing the candidate could otherwise project
        // both route sets, then have the candidate-removal notification skipped by an observer optimization.
        await RemoveProjectionsAsync(candidate, cancellationToken);
        var observerPublication = restoredPublicationId is null
            ? candidate
            : candidate with { PublicationId = restoredPublicationId };
        await NotifyTriggerObserversAsync(observerPublication, cancellationToken);
    }

    public async ValueTask RestoreAsync(
        PublicationRecord publication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        await PrepareAsync(publication, forceReplay: true, cancellationToken);
        await ActivateAsync(publication, replacedPublicationId: null, forceReplay: true, cancellationToken);
    }

    public async ValueTask RemoveAsync(PublicationRecord publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        await RemoveProjectionsAsync(publication, cancellationToken);
        await NotifyTriggerObserversAsync(publication, cancellationToken);
    }

    private async ValueTask RemoveProjectionsAsync(
        PublicationRecord publication,
        CancellationToken cancellationToken)
    {
        await DeliverAsync(
            publication.PublicationId,
            PublicationProjectionKinds.TriggerBindings,
            PublicationProjectionOperation.Remove,
            ct => triggerBindingStore.DeleteByActivationAsync(publication.PublicationId, ct),
            cancellationToken);

        if (recurringScheduleStore is not null)
            await DeliverAsync(
                publication.PublicationId,
                PublicationProjectionKinds.RecurringSchedules,
                PublicationProjectionOperation.Remove,
                ct => recurringScheduleStore.DeleteByActivationAsync(publication.PublicationId, ct),
                cancellationToken);
    }

    public static string BuildIntentId(
        string publicationId,
        string projectionKind,
        PublicationProjectionOperation operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionKind);
        return $"publication-projection:{publicationId.Length}:{publicationId}:{projectionKind.Length}:{projectionKind}:{operation}";
    }

    private async ValueTask DeliverAsync(
        string publicationId,
        string projectionKind,
        PublicationProjectionOperation operation,
        Func<CancellationToken, ValueTask> deliver,
        CancellationToken cancellationToken,
        bool forceReplay = false)
    {
        var intentId = BuildIntentId(publicationId, projectionKind, operation);
        var intent = await intentStore.FindAsync(intentId, cancellationToken);
        if (intent is null)
        {
            intent = new PublicationProjectionIntent(
                intentId,
                publicationId,
                projectionKind,
                operation,
                PublicationProjectionIntentStatus.Pending,
                AttemptCount: 0,
                NextAttemptAt: null,
                LastFailure: null);
            await intentStore.SaveAsync(intent, cancellationToken);
        }
        else if (intent.Status == PublicationProjectionIntentStatus.Delivered && !forceReplay)
            return;

        var delivering = intent with
        {
            Status = PublicationProjectionIntentStatus.Delivering,
            AttemptCount = intent.AttemptCount + 1,
            NextAttemptAt = null,
            LastFailure = null
        };
        var claimed = await intentStore.TryTransitionAsync(delivering, intent.Status, cancellationToken);
        if (!claimed.Succeeded)
        {
            var current = await intentStore.FindAsync(intentId, cancellationToken);
            if (current?.Status == PublicationProjectionIntentStatus.Delivered && !forceReplay)
                return;
            throw new InvalidOperationException($"Projection intent '{intentId}' could not be claimed for delivery.");
        }

        try
        {
            await deliver(cancellationToken);
            var delivered = delivering with
            {
                Status = PublicationProjectionIntentStatus.Delivered,
                NextAttemptAt = null,
                LastFailure = null
            };
            if (!(await intentStore.TryTransitionAsync(delivered, PublicationProjectionIntentStatus.Delivering, cancellationToken)).Succeeded)
                throw new InvalidOperationException($"Projection intent '{intentId}' completed but could not be recorded as delivered.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failed = delivering with
            {
                Status = PublicationProjectionIntentStatus.Failed,
                NextAttemptAt = timeProvider.GetUtcNow().Add(RetryDelay(delivering.AttemptCount)),
                LastFailure = new PublicationFailure("projection_delivery_failed", SafeMessage(exception))
            };
            await intentStore.TryTransitionAsync(failed, PublicationProjectionIntentStatus.Delivering, CancellationToken.None);
            throw;
        }
    }

    private async ValueTask NotifyTriggerObserversAsync(
        PublicationRecord publication,
        CancellationToken cancellationToken)
    {
        if (_triggerObservers.Count == 0)
            return;

        var bindings = await triggerBindingStore.ListAllByActivationAsync(publication.PublicationId, cancellationToken);
        var artifactId = bindings.FirstOrDefault()?.ArtifactId ?? publication.ArtifactId;
        // Authority can change while the artifact's extracted bindings remain identical (or empty), so consumers
        // must not apply the ordinary repeated-non-HTTP indexing skip to this notification.
        var snapshot = new Elsa.Workflows.Runtime.Core.Models.WorkflowTriggerIndexSnapshot(artifactId, bindings)
        {
            RequiresProjectionRefresh = true
        };
        foreach (var observer in _triggerObservers)
            await observer.OnTriggersIndexedAsync(snapshot, cancellationToken);
    }

    private static TimeSpan RetryDelay(int attemptCount) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(8, Math.Max(0, attemptCount - 1)))));

    private static string SafeMessage(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        return message.Length <= 512 ? message : message[..512];
    }
}
