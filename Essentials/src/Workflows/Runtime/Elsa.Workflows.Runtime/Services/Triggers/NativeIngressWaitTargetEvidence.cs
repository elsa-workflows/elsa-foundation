using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>Actual ordinary resume responsibility and positive retirement observations; absence remains unknown.</summary>
internal sealed class NativeIngressWaitTargetEvidence(IWorkflowSchedulerWorkQueue queue, IWorkflowExecutionStateStore executions,
    IActivityExecutionStateStore activities, IBookmarkStateStore bookmarks, TimeProvider time,
    CoalescingInner<IWorkflowSchedulerWorkQueue>? innerQueue = null,
    CoalescingInner<IWorkflowExecutionStateStore>? innerExecutions = null,
    CoalescingInner<IActivityExecutionStateStore>? innerActivities = null)
{
    internal IWorkflowSchedulerWorkQueue PhysicalQueue => queue is CoalescingWorkflowSchedulerWorkQueue
        ? innerQueue?.Value ?? throw Unsupported() : queue;
    internal IWorkflowExecutionStateStore PhysicalExecutions => executions is CoalescingWorkflowExecutionStateStore
        ? innerExecutions?.Value ?? throw Unsupported() : executions;
    internal IActivityExecutionStateStore PhysicalActivities => activities is CoalescingActivityExecutionStateStore
        ? innerActivities?.Value ?? throw Unsupported() : activities;
    internal IBookmarkStateStore PhysicalBookmarks => bookmarks;

    internal async ValueTask<RuntimeIngressWaitReceipt?> ReconcileAsync(RuntimePostCommitOutboxItem source,
        RuntimeIngressWaitRecipient recipient, RuntimeIngressWaitDispatchSeal seal, CancellationToken cancellationToken = default,
        Func<string, string, CancellationToken, ValueTask<BookmarkStateObservation?>>? observe = null)
    {
        var committed = new RuntimeCommittedIngressWait(source, recipient, seal);
        var envelope = RuntimeIngressWaitCommand.ReadEnvelope(seal);
        RuntimeIngressWaitCommand.ValidateEnvelope(committed, envelope);
        var expected = recipient.Expected; var capture = expected.RegistrationCapture!;
        var page = await PhysicalQueue.ListAsync(new(recipient.Identity.WorkflowExecutionId, RuntimeStorePageRequest.MaximumLimit), cancellationToken);
        if (page.Items.SingleOrDefault(item => item.WorkItemId == envelope.EnvelopeId) is { } queued)
        {
            RuntimeIngressWaitCommand.ValidateWorkItem(committed, queued);
            return Receipt(RuntimeIngressWaitAcceptanceKind.SchedulerCommand, queued.WorkItemId);
        }
        // A queue row is removed only after its consumed trigger claim is physically committed.
        var execution = await PhysicalExecutions.FindAsync(expected.WorkflowExecutionId, cancellationToken);
        if (execution is null || !expected.MatchesExecution(execution, recipient.Identity.Scope)) return null;
        var activity = await PhysicalActivities.FindAsync(expected.WorkflowExecutionId, expected.ActivityExecutionId, cancellationToken);
        if (activity is null || activity.Execution.WorkflowExecutionId != expected.WorkflowExecutionId ||
            activity.Execution.ActivityExecutionId != expected.ActivityExecutionId || activity.Execution.ExecutableNodeId != expected.ExecutableNodeId) return null;
        var payload = envelope.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        var typed = payload.TriggerDelivery;
        if (activity.Metadata.GetValueOrDefault(RuntimeIngressWaitCommand.AcceptanceSealMetadataKey) == seal.Fingerprint &&
            activity.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId) == envelope.EnvelopeId &&
            activity.TriggerDeliveries?.Any(item => item.Status == ActivityTriggerDeliveryStatus.Consumed &&
                item.DeliveryId == envelope.Command.CommandId && item.RegistrationId == expected.BookmarkId &&
                item.DeduplicationKey == envelope.IdempotencyKey && item.ReceivedAt == envelope.EnqueuedAt &&
                (typed is null || item.ProviderId == typed.ProviderId && NativeIngressTargetEvidence.Equal(item.PayloadType, typed.PayloadType))) == true)
            return Receipt(RuntimeIngressWaitAcceptanceKind.ConsumptionCheckpoint, activity.Execution.ActivityExecutionId);

        var observation = await (observe is null
            ? PhysicalBookmarks.ObserveAsync(expected.WorkflowExecutionId, expected.BookmarkId, cancellationToken)
            : observe(expected.WorkflowExecutionId, expected.BookmarkId, cancellationToken));
        if (observation?.Scope is { } observedScope && observedScope != recipient.Identity.Scope) return null;
        if (observation is not null && observation.Generation > capture.Generation)
            return Receipt(RuntimeIngressWaitAcceptanceKind.RetiredGeneration, expected.BookmarkId,
                new(RuntimeIngressWaitRetirementKind.ReplacedGeneration, observation.Generation, null, null));
        var now = time.GetUtcNow();
        // Expiration is an affirmative immutable condition on the accepted exact registration.
        if (expected.ExpiresAt is { } expiresAt && expiresAt <= now || capture.Scope.TestScope is { } test && test.IsExpired(now))
            return Receipt(RuntimeIngressWaitAcceptanceKind.RetiredGeneration, expected.BookmarkId,
                new(RuntimeIngressWaitRetirementKind.ExpiredRegistration, null, expected.ExpiresAt ?? capture.Scope.TestScope!.ExpiresAt, null));
        if (activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered)
            return Receipt(RuntimeIngressWaitAcceptanceKind.RetiredGeneration, activity.Execution.ActivityExecutionId,
                new(RuntimeIngressWaitRetirementKind.RetiredOwner, null, null, activity.Status));
        return null;

        RuntimeIngressWaitReceipt Receipt(RuntimeIngressWaitAcceptanceKind kind, string evidenceId, RuntimeIngressWaitRetirement? retirement = null) =>
            new(recipient.WaitId, seal.Fingerprint, recipient.Identity, capture.Executable, kind, evidenceId, time.GetUtcNow(), retirement);
    }

    internal static RuntimeIngressWaitOrigin? Origin(WorkflowExecutionCommandEnvelope envelope) =>
        envelope.Command.Kind == WorkflowExecutionCommandKind.ResumeBookmark && envelope.Command.Payload is { } payload
            ? payload.Deserialize<RuntimeResumeBookmarkCommandPayload>()?.IngressWaitOrigin : null;
    private static NotSupportedException Unsupported() => new("The selected physical ordinary wait evidence boundary is unsupported.");
}
