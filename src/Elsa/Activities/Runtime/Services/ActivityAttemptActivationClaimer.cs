using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Runtime.Services;

internal static class ActivityAttemptActivationClaimer
{
    internal const int MaxRetainedAttempts = 32;
    internal const int MaxRetainedTriggerDeliveries = 32;
    public static ValueTask<ActivityAttemptActivationClaim> ClaimInvokeAsync(
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload payload,
        ActivityExecutionState state,
        Elsa.Activities.Runtime.Core.Models.SideEffectProfile sideEffectProfile,
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
            sideEffectProfile,
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
        Elsa.Activities.Runtime.Core.Models.SideEffectProfile sideEffectProfile,
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
            sideEffectProfile,
            freshAttemptReason: ActivityAttemptReason.Resume,
            claimedReplacementReason: ActivityAttemptReason.Retry,
            triggerDeliveryId: null,
            requireFreshAttempt: true,
            triggerDelivery: null,
            cancellationToken);

    public static ActivityAttemptActivationClaim PrepareTypedResumeClaim(
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        RuntimeResumeBookmarkCommandPayload payload,
        ActivityExecutionState state,
        ActivityTriggerDelivery triggerDelivery)
    {
        if (state.Status != ActivityExecutionStatus.Suspended)
            throw new InvalidOperationException($"VF-ACT-008: Activity invocation '{state.InvocationId}' is not suspended and cannot claim a trigger delivery.");
        if (state.Completion is not null)
            throw new InvalidOperationException($"VF-ACT-007: Completed activity invocation '{state.InvocationId}' cannot create another attempt.");

        var occurredAt = timeProvider.GetUtcNow();
        var attempts = state.Attempts?.OrderBy(attempt => attempt.Ordinal).ToArray() ?? [];
        var openAttempt = attempts.LastOrDefault(attempt => attempt.EndedAt is null);
        if (openAttempt is not null)
        {
            var endedAttempt = EndAttempt(openAttempt, Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, occurredAt);
            attempts = attempts
                .Where(attempt => !StringComparer.Ordinal.Equals(attempt.AttemptId, endedAttempt.AttemptId))
                .Append(endedAttempt)
                .OrderBy(attempt => attempt.Ordinal)
                .ToArray();
        }

        var ordinal = NextAttemptOrdinal(state, attempts);
        var attempt = new ActivityAttempt(
            $"{state.InvocationId}:attempt:{ordinal}",
            state.InvocationId,
            ordinal,
            ActivityAttemptReason.Resume,
            occurredAt,
            triggerDeliveryId: triggerDelivery.DeliveryId);
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim] = attempt.AttemptId;
        metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = workItem.WorkItemId;
        var consumedDelivery = WithDeliveryStatus(triggerDelivery, ActivityTriggerDeliveryStatus.Consumed);
        var claimedState = CompactAttemptHistory(state with
        {
            Status = ActivityExecutionStatus.Running,
            SubStatus = null,
            Attempts = attempts.Append(attempt).ToArray(),
            TriggerDeliveries = AppendTriggerDelivery(state.TriggerDeliveries, consumedDelivery),
            TriggerRegistrations = [],
            BookmarkIds = [],
            Metadata = RuntimeModelMetadata.Snapshot(metadata)
        });
        return new ActivityAttemptActivationClaim(claimedState, attempt);
    }

    public static ActivityAttemptActivationClaim PrepareTypedResumeRedeliveryClaim(
        TimeProvider timeProvider,
        RuntimeSchedulerWorkItem workItem,
        ActivityExecutionState state,
        ActivityTriggerDelivery consumedDelivery)
    {
        if (state.Status != ActivityExecutionStatus.Running || consumedDelivery.Status != ActivityTriggerDeliveryStatus.Consumed)
            throw new InvalidOperationException($"VF-ACT-008: Activity invocation '{state.InvocationId}' has no consumed running resume claim to redeliver.");
        if (!state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId, out var claimedWorkItemId) ||
            !StringComparer.Ordinal.Equals(claimedWorkItemId, workItem.WorkItemId) ||
            !state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var claimedAttemptId))
            throw new InvalidOperationException($"VF-ACT-008: Activity invocation '{state.InvocationId}' is not claimed by resume work item '{workItem.WorkItemId}'.");

        var occurredAt = timeProvider.GetUtcNow();
        var attempts = state.Attempts?.OrderBy(attempt => attempt.Ordinal).ToArray() ?? [];
        var abandonedAttempt = attempts.SingleOrDefault(attempt =>
            attempt.EndedAt is null && StringComparer.Ordinal.Equals(attempt.AttemptId, claimedAttemptId));
        if (abandonedAttempt is null || !StringComparer.Ordinal.Equals(abandonedAttempt.TriggerDeliveryId, consumedDelivery.DeliveryId))
            throw new InvalidOperationException($"VF-ACT-008: Activity invocation '{state.InvocationId}' has no open attempt for consumed delivery '{consumedDelivery.DeliveryId}'.");

        var endedAttempt = EndAttempt(abandonedAttempt, Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, occurredAt);
        attempts = attempts
            .Where(attempt => !StringComparer.Ordinal.Equals(attempt.AttemptId, endedAttempt.AttemptId))
            .Append(endedAttempt)
            .OrderBy(attempt => attempt.Ordinal)
            .ToArray();
        var ordinal = NextAttemptOrdinal(state, attempts);
        var replacementAttempt = new ActivityAttempt(
            $"{state.InvocationId}:attempt:{ordinal}",
            state.InvocationId,
            ordinal,
            ActivityAttemptReason.Resume,
            occurredAt,
            triggerDeliveryId: consumedDelivery.DeliveryId);
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim] = replacementAttempt.AttemptId;
        metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = workItem.WorkItemId;
        var claimedState = CompactAttemptHistory(state with
        {
            Attempts = attempts.Append(replacementAttempt).ToArray(),
            Metadata = RuntimeModelMetadata.Snapshot(metadata)
        });
        return new ActivityAttemptActivationClaim(claimedState, replacementAttempt);
    }

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
        return CompactAttemptHistory(state with
        {
            Attempts = attempts
                .Where(attempt => !StringComparer.Ordinal.Equals(attempt.AttemptId, endedAttempt.AttemptId))
                .Append(endedAttempt)
                .OrderBy(attempt => attempt.Ordinal)
                .ToArray()
        });
    }

    public static ActivityExecutionState MarkInitialActivationCompleted(ActivityExecutionState state, string workItemId)
    {
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId] = workItemId;
        return state with { Metadata = RuntimeModelMetadata.Snapshot(metadata) };
    }

    public static bool WasInitialActivationCompleted(ActivityExecutionState state, string workItemId) =>
        state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId, out var completedWorkItemId) &&
        StringComparer.Ordinal.Equals(completedWorkItemId, workItemId);

    public static ActivityExecutionState MarkParentCompletionProcessed(ActivityExecutionState completedChildState, string workItemId)
    {
        var metadata = completedChildState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId] = workItemId;
        return completedChildState with { Metadata = RuntimeModelMetadata.Snapshot(metadata) };
    }

    public static bool WasParentCompletionProcessed(ActivityExecutionState completedChildState) =>
        completedChildState.Metadata.TryGetValue(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId, out var workItemId) &&
        !string.IsNullOrWhiteSpace(workItemId);

    internal static ActivityExecutionState CompactAttemptHistory(ActivityExecutionState state)
    {
        var attempts = state.Attempts?.OrderBy(attempt => attempt.Ordinal).ToArray() ?? [];
        if (attempts.Length == 0)
            return state;

        var highWatermark = attempts.Max(attempt => attempt.Ordinal);
        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptOrdinalHighWatermark, out var serializedHighWatermark) &&
            int.TryParse(serializedHighWatermark, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var persistedHighWatermark))
            highWatermark = Math.Max(highWatermark, persistedHighWatermark);

        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.ActivityAttemptOrdinalHighWatermark] = highWatermark.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Keep the first attempt as invocation-origin evidence and the newest attempts for current diagnostics.
        // The high-watermark preserves monotonic IDs without repeatedly sorting/serializing the full invocation history.
        var retained = attempts.Length <= MaxRetainedAttempts
            ? attempts
            : attempts.Take(1).Concat(attempts.TakeLast(MaxRetainedAttempts - 1)).ToArray();
        return CompactTriggerDeliveryHistory(state with
        {
            Attempts = retained,
            Metadata = RuntimeModelMetadata.Snapshot(metadata)
        });
    }

    /// <summary>
    /// Bounds durable trigger-delivery diagnostics while preserving every delivery still needed by an open
    /// activation claim or live registration. Historical records retain routing/deduplication identity but drop
    /// their payload once no attempt can consume it.
    /// </summary>
    internal static ActivityExecutionState CompactTriggerDeliveryHistory(ActivityExecutionState state)
    {
        var deliveries = state.TriggerDeliveries?
            .OrderBy(delivery => delivery.ReceivedAt)
            .ThenBy(delivery => delivery.DeliveryId, StringComparer.Ordinal)
            .ToArray() ?? [];
        if (deliveries.Length == 0)
            return state;

        var liveRegistrationIds = (state.TriggerRegistrations ?? [])
            .Select(registration => registration.RegistrationId)
            .ToHashSet(StringComparer.Ordinal);
        var openAttemptDeliveryIds = (state.Attempts ?? [])
            .Where(attempt => attempt.EndedAt is null && attempt.TriggerDeliveryId is not null)
            .Select(attempt => attempt.TriggerDeliveryId!)
            .ToHashSet(StringComparer.Ordinal);
        var diagnosticDeliveryIds = (state.Attempts ?? [])
            .Where(attempt => attempt.TriggerDeliveryId is not null)
            .Select(attempt => attempt.TriggerDeliveryId!)
            .ToHashSet(StringComparer.Ordinal);
        var required = deliveries
            .Where(delivery =>
                liveRegistrationIds.Contains(delivery.RegistrationId) ||
                openAttemptDeliveryIds.Contains(delivery.DeliveryId) ||
                diagnosticDeliveryIds.Contains(delivery.DeliveryId))
            .ToArray();
        if (required.Length > MaxRetainedTriggerDeliveries)
        {
            throw new InvalidOperationException(
                $"VF-ACT-008: Activity invocation '{state.InvocationId}' requires {required.Length} trigger-delivery records, exceeding the limit of {MaxRetainedTriggerDeliveries}.");
        }

        var retainedIds = required
            .Select(delivery => delivery.DeliveryId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var historical in deliveries
                     .Where(delivery => !retainedIds.Contains(delivery.DeliveryId))
                     .TakeLast(MaxRetainedTriggerDeliveries - required.Length))
            retainedIds.Add(historical.DeliveryId);

        var retained = deliveries
            .Where(delivery => retainedIds.Contains(delivery.DeliveryId))
            .Select(delivery =>
                liveRegistrationIds.Contains(delivery.RegistrationId) || openAttemptDeliveryIds.Contains(delivery.DeliveryId)
                    ? delivery
                    : WithoutPayload(delivery))
            .ToArray();
        return state with { TriggerDeliveries = retained };
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
        Elsa.Activities.Runtime.Core.Models.SideEffectProfile sideEffectProfile,
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
            var ordinal = NextAttemptOrdinal(state, attempts);
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
        var claimedState = CompactAttemptHistory(state with
        {
            Attempts = attempts,
            TriggerDeliveries = AppendTriggerDelivery(state.TriggerDeliveries, triggerDelivery),
            Metadata = RuntimeModelMetadata.Snapshot(activityMetadata)
        });
        var checkpointMetadata = RuntimeModelMetadata.Snapshot(new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = checkpointReason,
            // ADR 0032 R2: the claim boundary stays Mandatory (IsMandatoryCheckpoint forbids only Skip, never
            // Deferred), so the committer's guardrail is preserved for both profiles while ReplaySafe still
            // permits the coalescing policy to Defer this flush. The resolved profile rides as transport so the
            // policy can make that decision; the contract remains the source of truth.
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.CheckpointSideEffectProfile] = sideEffectProfile == Elsa.Activities.Runtime.Core.Models.SideEffectProfile.ReplaySafe
                ? RuntimeMetadataKeys.CheckpointSideEffectProfileReplaySafe
                : RuntimeMetadataKeys.CheckpointSideEffectProfileExternal,
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

    private static int NextAttemptOrdinal(ActivityExecutionState state, IReadOnlyCollection<ActivityAttempt> attempts)
    {
        var highWatermark = attempts.Count == 0 ? 0 : attempts.Max(attempt => attempt.Ordinal);
        if (state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptOrdinalHighWatermark, out var serializedHighWatermark) &&
            int.TryParse(serializedHighWatermark, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var persistedHighWatermark))
            highWatermark = Math.Max(highWatermark, persistedHighWatermark);

        return checked(highWatermark + 1);
    }

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

    private static ActivityTriggerDelivery WithDeliveryStatus(
        ActivityTriggerDelivery delivery,
        ActivityTriggerDeliveryStatus status) =>
        new(
            delivery.DeliveryId,
            delivery.RegistrationId,
            delivery.PayloadType,
            delivery.Payload,
            delivery.ProviderId,
            delivery.ReceivedAt,
            delivery.DeduplicationKey,
            status);

    private static ActivityTriggerDelivery WithoutPayload(ActivityTriggerDelivery delivery) =>
        new(
            delivery.DeliveryId,
            delivery.RegistrationId,
            delivery.PayloadType,
            ValueEnvelope.Absent(delivery.PayloadType, delivery.Payload.Policy),
            delivery.ProviderId,
            delivery.ReceivedAt,
            delivery.DeduplicationKey,
            ActivityTriggerDeliveryStatus.Consumed);
}

internal sealed record ActivityAttemptActivationClaim(ActivityExecutionState State, ActivityAttempt Attempt);
