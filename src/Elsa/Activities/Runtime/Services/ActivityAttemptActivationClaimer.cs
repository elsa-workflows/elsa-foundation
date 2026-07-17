using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

internal static class ActivityAttemptActivationClaimer
{
    public static ValueTask<ActivityAttemptActivationClaim> ClaimInvokeAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload payload,
        ActivityExecutionState state,
        CancellationToken cancellationToken) =>
        ClaimAsync(
            checkpointCommitter,
            timeProvider,
            workItem,
            payload.PinnedExecutable,
            payload.ExecutableNodeId,
            payload.ActivityExecutionId,
            payload.Reason,
            state,
            freshAttemptReason: ActivityAttemptReason.Retry,
            claimedReplacementReason: ActivityAttemptReason.Retry,
            triggerDeliveryId: null,
            requireFreshAttempt: false,
            triggerDelivery: null,
            cancellationToken);

    public static ValueTask<ActivityAttemptActivationClaim> ClaimStructuralCallbackAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        RuntimeCompleteActivityCommandPayload payload,
        ActivityExecutionState state,
        CancellationToken cancellationToken) =>
        ClaimAsync(
            checkpointCommitter,
            timeProvider,
            workItem,
            payload.PinnedExecutable,
            payload.ExecutableNodeId,
            payload.ActivityExecutionId,
            payload.Reason,
            state,
            freshAttemptReason: ActivityAttemptReason.Resume,
            claimedReplacementReason: ActivityAttemptReason.Retry,
            triggerDeliveryId: null,
            requireFreshAttempt: true,
            triggerDelivery: null,
            cancellationToken);

    public static ValueTask<ActivityAttemptActivationClaim> ClaimTypedResumeAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload payload,
        ActivityExecutionState state,
        ActivityTriggerDelivery triggerDelivery,
        CancellationToken cancellationToken) =>
        ClaimAsync(
            checkpointCommitter,
            timeProvider,
            workItem,
            payload.PinnedExecutable,
            payload.ExecutableNodeId,
            payload.ActivityExecutionId,
            payload.Reason,
            state,
            freshAttemptReason: ActivityAttemptReason.Resume,
            claimedReplacementReason: ActivityAttemptReason.Resume,
            triggerDeliveryId: triggerDelivery.DeliveryId,
            requireFreshAttempt: true,
            triggerDelivery,
            cancellationToken);

    public static ActivityExecutionState EndOpenAttempt(
        ActivityExecutionState state,
        Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind transitionKind,
        DateTimeOffset endedAt)
    {
        var attempts = state.Attempts?.OrderBy(attempt => attempt.Ordinal).ToArray() ?? [];
        var openAttempt = attempts.LastOrDefault(attempt => attempt.EndedAt is null);
        if (openAttempt is null)
            return state;

        var endedAttempt = EndAttempt(openAttempt, transitionKind, endedAt);
        return state with
        {
            Attempts = attempts
                .Where(attempt => !StringComparer.Ordinal.Equals(attempt.AttemptId, endedAttempt.AttemptId))
                .Append(endedAttempt)
                .OrderBy(attempt => attempt.Ordinal)
                .ToArray()
        };
    }

    public static ActivityExecutionState MarkActivationCompleted(ActivityExecutionState state, string workItemId)
    {
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var completedWorkItemIds = ReadCompletedWorkItemIds(metadata);
        if (!completedWorkItemIds.Contains(workItemId, StringComparer.Ordinal))
            completedWorkItemIds = completedWorkItemIds.Append(workItemId).ToArray();

        metadata[RuntimeMetadataKeys.ActivityActivationCompletedWorkItemIds] = JsonSerializer.Serialize(completedWorkItemIds);
        return state with { Metadata = RuntimeModelMetadata.Snapshot(metadata) };
    }

    public static bool WasActivationCompleted(ActivityExecutionState state, string workItemId) =>
        ReadCompletedWorkItemIds(state.Metadata).Contains(workItemId, StringComparer.Ordinal);

    private static IReadOnlyCollection<string> ReadCompletedWorkItemIds(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(RuntimeMetadataKeys.ActivityActivationCompletedWorkItemIds, out var serialized))
            return [];

        try
        {
            var workItemIds = JsonSerializer.Deserialize<string[]>(serialized)
                              ?? throw new InvalidOperationException("Activity activation completion history resolved to null.");
            if (workItemIds.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Activity activation completion history contains a blank scheduler work-item ID.");

            return workItemIds.Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Activity activation completion history is invalid.", exception);
        }
    }

    private static async ValueTask<ActivityAttemptActivationClaim> ClaimAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        string executableNodeId,
        string activityExecutionId,
        string checkpointReason,
        ActivityExecutionState state,
        ActivityAttemptReason freshAttemptReason,
        ActivityAttemptReason claimedReplacementReason,
        string? triggerDeliveryId,
        bool requireFreshAttempt,
        ActivityTriggerDelivery? triggerDelivery,
        CancellationToken cancellationToken)
    {
        if (state.Completion is not null)
            throw new InvalidOperationException($"VF-ACT-007: Completed activity invocation '{state.InvocationId}' cannot create another attempt.");

        var occurredAt = timeProvider.GetUtcNow();
        var attempts = state.Attempts?.OrderBy(attempt => attempt.Ordinal).ToArray() ?? [];
        var openAttempt = attempts.LastOrDefault(attempt => attempt.EndedAt is null);
        var openAttemptWasClaimed = openAttempt is not null &&
            state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claimedAttemptId) &&
            StringComparer.Ordinal.Equals(claimedAttemptId, openAttempt.AttemptId);

        if (openAttempt is not null && (openAttemptWasClaimed || requireFreshAttempt))
        {
            var transitionKind = openAttemptWasClaimed
                ? Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault
                : Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend;
            var endedAttempt = EndAttempt(openAttempt, transitionKind, occurredAt);
            attempts = attempts
                .Where(attempt => !StringComparer.Ordinal.Equals(attempt.AttemptId, endedAttempt.AttemptId))
                .Append(endedAttempt)
                .OrderBy(attempt => attempt.Ordinal)
                .ToArray();
            openAttempt = null;
        }

        if (openAttempt is null)
        {
            var reason = openAttemptWasClaimed ? claimedReplacementReason : freshAttemptReason;
            var deliveryId = reason == ActivityAttemptReason.Resume ? triggerDeliveryId : null;
            var ordinal = attempts.Length == 0 ? 1 : attempts.Max(attempt => attempt.Ordinal) + 1;
            openAttempt = new ActivityAttempt(
                $"{state.InvocationId}:attempt:{ordinal}",
                state.InvocationId,
                ordinal,
                reason,
                occurredAt,
                triggerDeliveryId: deliveryId);
            attempts = attempts.Append(openAttempt).ToArray();
        }

        var activityMetadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        activityMetadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim] = openAttempt.AttemptId;
        activityMetadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = workItem.WorkItemId;
        var claimedState = state with
        {
            Attempts = attempts,
            TriggerDeliveries = AppendTriggerDelivery(state.TriggerDeliveries, triggerDelivery),
            Metadata = RuntimeModelMetadata.Snapshot(activityMetadata)
        };
        var checkpointMetadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = checkpointReason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = activityExecutionId,
            [RuntimeMetadataKeys.ActivityAttemptActivationClaim] = openAttempt.AttemptId,
            [RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.ExecutableNodeId] = executableNodeId,
            [RuntimeMetadataKeys.ExecutableArtifactId] = pinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = pinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = pinnedExecutable.ArtifactHash
        });
        var checkpointSuffix = $"activity-attempt-claimed:{activityExecutionId}:{openAttempt.AttemptId}";
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workItem.WorkItemId}:{checkpointSuffix}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{workItem.WorkItemId}:{checkpointSuffix}",
                Name: RuntimeCheckpointNames.ActivityAttemptClaimed,
                WorkflowExecutionId: workItem.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [activityExecutionId],
                Metadata: checkpointMetadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: activityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: claimedState,
                        Metadata: checkpointMetadata)
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: []),
            PostCommitIntents: [],
            Metadata: checkpointMetadata);

        await checkpointCommitter.CommitAsync(commit, cancellationToken);
        return new ActivityAttemptActivationClaim(claimedState, openAttempt);
    }

    private static ActivityAttempt EndAttempt(
        ActivityAttempt attempt,
        Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind transitionKind,
        DateTimeOffset endedAt) =>
        new(
            attempt.AttemptId,
            attempt.InvocationId,
            attempt.Ordinal,
            attempt.Reason,
            attempt.StartedAt,
            endedAt,
            attempt.TriggerDeliveryId,
            transitionKind);

    private static IReadOnlyCollection<ActivityTriggerDelivery>? AppendTriggerDelivery(
        IReadOnlyCollection<ActivityTriggerDelivery>? deliveries,
        ActivityTriggerDelivery? delivery)
    {
        if (delivery is null)
            return deliveries;

        var existing = deliveries ?? [];
        return existing.Any(candidate => StringComparer.Ordinal.Equals(candidate.DeliveryId, delivery.DeliveryId))
            ? existing
            : existing.Append(delivery).ToArray();
    }
}

internal sealed record ActivityAttemptActivationClaim(ActivityExecutionState State, ActivityAttempt Attempt);
