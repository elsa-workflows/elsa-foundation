using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

internal sealed record JobNativePreWaitCancellationReceipt(int FormatVersion, string CommitId, string CheckpointId,
    string SchedulerWorkItemId, string CommandId, DateTimeOffset OccurredAt, string Sha256);

/// <summary>Positive ordinary cancellation proof for an initial activation claim that never committed a Jobs handoff.</summary>
internal static class JobsNativePreWaitCancellation
{
    internal const string MetadataKey = "jobs.native.pre-wait-cancel.v1";

    internal static bool IsInitialClaim(ActivityExecutionState activity, ActivityExecutionStatus status) =>
        activity.Execution.ActivityType == JobsNativeBoundary.ActivityType && activity.Status == status &&
        activity.ParentActivityExecutionId is null && activity.SupersededByActivityExecutionId is null &&
        activity.ContractIdentity is { ActivityTypeKey: JobsNativeBoundary.ActivityType } && activity.PrivateState is null &&
        activity.Completion is null && activity.Fault is null && activity.FaultCount == 0 && activity.AggregateFaultCount == 0 && activity.IncidentIds.Count == 0 &&
        activity.BookmarkIds.Count == 0 && activity.TriggerRegistrations?.Count is not > 0 && activity.TriggerDeliveries?.Count is not > 0 &&
        !activity.Metadata.ContainsKey(JobsNativeBoundary.AdmissionMetadataKey) && !activity.Metadata.ContainsKey(JobsNativeBoundary.DeliveryMetadataKey) &&
        !activity.Metadata.ContainsKey(RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId) &&
        activity.Attempts is { Count: 1 } attempts && attempts.Single() is
            { Reason: ActivityAttemptReason.Initial, Ordinal: 1, EndedAt: null, TransitionKind: null, TriggerDeliveryId: null, IncidentId: null } attempt &&
        attempt.InvocationId == activity.InvocationId && activity.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityAttemptActivationClaim) == attempt.AttemptId &&
        Bounded(activity.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId));

    internal static bool IsCancellationSource(RuntimeCheckpointCommit commit, WorkflowExecutionState parent)
    {
        var workItem = commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SchedulerWorkItemId);
        return parent.Status == WorkflowExecutionStatus.Cancelled && parent.CompletedAt == commit.Checkpoint.OccurredAt &&
            parent.WorkflowExecutionId == commit.WorkflowExecutionId && commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCancelled &&
            commit.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CommandKind) == nameof(WorkflowExecutionCommandKind.Cancel) &&
            commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CheckpointReason) == "WorkflowCancellation" && Bounded(workItem) &&
            commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SchedulerWorkItemId) == workItem &&
            Bounded(commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CommandId)) &&
            commit.CommitId == $"commit:{workItem}:cancel" && commit.Checkpoint.CheckpointId == $"checkpoint:{workItem}:cancel";
    }

    internal static JobNativePreWaitCancellationReceipt Create(RuntimeCheckpointCommit commit, WorkflowExecutionState parent, ActivityExecutionState activity)
    {
        if (!IsCancellationSource(commit, parent) || !IsInitialClaim(activity, ActivityExecutionStatus.Cancelled) ||
            activity.CompletedAt != parent.CompletedAt || activity.Execution.WorkflowExecutionId != parent.WorkflowExecutionId ||
            !commit.Checkpoint.ActivityExecutionIds.Contains(activity.InvocationId, StringComparer.Ordinal) ||
            commit.StateChanges.Bookmarks.Any(change => change.Operation == RuntimeStateChangeOperation.Upsert && change.State.ActivityExecutionId == activity.InvocationId) ||
            commit.PostCommitIntents.Any(intent => intent.ActivityExecutionId == activity.InvocationId &&
                intent.Kind is JobsNativeBoundary.StartKind or JobsNativeBoundary.CancelKind or JobsNativeBoundary.SettlementKind)) throw JobsNativeBoundary.Invalid();
        var receipt = new JobNativePreWaitCancellationReceipt(1, commit.CommitId, commit.Checkpoint.CheckpointId,
            commit.Metadata[RuntimeMetadataKeys.SchedulerWorkItemId], commit.Checkpoint.Metadata[RuntimeMetadataKeys.CommandId],
            commit.Checkpoint.OccurredAt, string.Empty);
        return receipt with { Sha256 = Fingerprint(parent, activity, receipt) };
    }

    internal static bool Matches(WorkflowExecutionState parent, ActivityExecutionState activity)
    {
        if (!activity.Metadata.TryGetValue(MetadataKey, out var json)) return false;
        try
        {
            var receipt = JobsNativeBoundary.ReadString<JobNativePreWaitCancellationReceipt>(json);
            JobsNativeBoundary.Digest(receipt.Sha256);
            return receipt.FormatVersion == 1 && Bounded(receipt.SchedulerWorkItemId) && Bounded(receipt.CommandId) &&
                receipt.CommitId == $"commit:{receipt.SchedulerWorkItemId}:cancel" && receipt.CheckpointId == $"checkpoint:{receipt.SchedulerWorkItemId}:cancel" &&
                parent.Status == WorkflowExecutionStatus.Cancelled && parent.CompletedAt == receipt.OccurredAt &&
                activity.CompletedAt == receipt.OccurredAt && activity.Execution.WorkflowExecutionId == parent.WorkflowExecutionId &&
                IsInitialClaim(activity, ActivityExecutionStatus.Cancelled) && activity.Attempts!.Single().StartedAt <= receipt.OccurredAt &&
                receipt.Sha256 == Fingerprint(parent, activity, receipt);
        }
        catch (Elsa.Jobs.Core.JobException) { return false; }
    }

    private static string Fingerprint(WorkflowExecutionState parent, ActivityExecutionState activity, JobNativePreWaitCancellationReceipt receipt) =>
        JobsNativeBoundary.Hash(new
        {
            Format = MetadataKey, receipt.CommitId, receipt.CheckpointId, receipt.SchedulerWorkItemId, receipt.CommandId, receipt.OccurredAt,
            Scope = new Elsa.Jobs.Core.JobScope(parent.Partition?.Value ?? throw JobsNativeBoundary.Invalid(), parent.TenantId),
            parent.WorkflowExecutionId, parent.PinnedExecutable, parent.ModelWorkOrigin, ParentStatus = parent.Status, ParentCompletedAt = parent.CompletedAt,
            activity.Execution, activity.ContractIdentity, activity.OperationKey, ActivityStatus = activity.Status, ActivityCompletedAt = activity.CompletedAt, activity.Attempts,
            Claim = activity.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim],
            ClaimWorkItem = activity.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId]
        });

    private static bool Bounded(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4096;
}
