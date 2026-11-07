using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsNativePreWaitCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_cancel_checkpoint_retains_positive_initial_claim_proof_without_inventing_a_job_or_ending_the_attempt(bool modelWorker)
    {
        var (parent, activity, commit) = Cancellation(modelWorker);
        var enricher = new JobsCheckpointEnricher(new ActivityStore());
        var saved = await enricher.EnrichAsync(commit);
        await enricher.ValidateFinalAsync(saved);
        var marked = Assert.Single(saved.StateChanges.ActivityExecutions).State;
        Assert.True(JobsNativePreWaitCancellation.Matches(parent, marked));
        Assert.Null(Assert.Single(marked.Attempts!).EndedAt); Assert.Null(marked.PrivateState); Assert.Empty(marked.BookmarkIds);
        Assert.Empty(saved.PostCommitIntents); Assert.False(marked.Metadata.ContainsKey(JobsNativeBoundary.AdmissionMetadataKey));
        var receipt = JobsNativeBoundary.ReadString<JobNativePreWaitCancellationReceipt>(marked.Metadata[JobsNativePreWaitCancellation.MetadataKey]);
        Assert.Equal(commit.CommitId, receipt.CommitId); Assert.Equal(commit.Checkpoint.CheckpointId, receipt.CheckpointId);
        Assert.Equal("cancel-command", receipt.CommandId); Assert.Equal(parent.CompletedAt, receipt.OccurredAt);
        Assert.False(JobsNativePreWaitCancellation.Matches(parent, activity));
    }

    [Theory]
    [InlineData("checkpoint")]
    [InlineData("commit")]
    [InlineData("command")]
    [InlineData("work-item")]
    [InlineData("parent-time")]
    public async Task Missing_or_different_cancellation_source_cannot_create_the_phase_receipt(string changed)
    {
        var (_, _, commit) = Cancellation(true);
        var metadata = commit.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        var checkpointMetadata = commit.Checkpoint.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (changed == "command") metadata[RuntimeMetadataKeys.CommandKind] = nameof(WorkflowExecutionCommandKind.InvokeActivity);
        if (changed == "work-item") checkpointMetadata[RuntimeMetadataKeys.SchedulerWorkItemId] = "different-source";
        commit = commit with { Metadata = metadata, CommitId = changed == "commit" ? "different-commit" : commit.CommitId,
            Checkpoint = commit.Checkpoint with { Name = changed == "checkpoint" ? RuntimeCheckpointNames.BookmarkCreated : commit.Checkpoint.Name,
                OccurredAt = changed == "parent-time" ? commit.Checkpoint.OccurredAt.AddSeconds(1) : commit.Checkpoint.OccurredAt,
                Metadata = checkpointMetadata } };
        var saved = await new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(commit);
        Assert.False(Assert.Single(saved.StateChanges.ActivityExecutions).State.Metadata.ContainsKey(JobsNativePreWaitCancellation.MetadataKey));
        Assert.Empty(saved.PostCommitIntents);
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("claim-work-item")]
    [InlineData("activation-completed")]
    [InlineData("retry")]
    [InlineData("ended")]
    [InlineData("wait")]
    [InlineData("private-state")]
    [InlineData("admission")]
    [InlineData("delivery")]
    [InlineData("contract")]
    [InlineData("nested")]
    public async Task A_saved_phase_receipt_cannot_hide_missing_claims_retries_waits_or_other_invocation_effects(string changed)
    {
        var (parent, _, commit) = Cancellation(true);
        var saved = await new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(commit);
        var activity = Assert.Single(saved.StateChanges.ActivityExecutions).State;
        var metadata = activity.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        switch (changed)
        {
            case "claim": metadata.Remove(RuntimeMetadataKeys.ActivityAttemptActivationClaim); break;
            case "claim-work-item": metadata.Remove(RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId); break;
            case "activation-completed": metadata[RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId] = "completed"; break;
            case "admission": metadata[JobsNativeBoundary.AdmissionMetadataKey] = "{}"; break;
            case "delivery": metadata[JobsNativeBoundary.DeliveryMetadataKey] = "{}"; break;
        }
        var original = Assert.Single(activity.Attempts!);
        activity = activity with { Metadata = metadata };
        activity = changed switch
        {
            "retry" => activity with { Attempts = [new(original.AttemptId, activity.InvocationId, 1, ActivityAttemptReason.Retry, original.StartedAt)] },
            "ended" => activity with { Attempts = [new(original.AttemptId, activity.InvocationId, 1, ActivityAttemptReason.Initial,
                original.StartedAt, activity.CompletedAt, transitionKind: ActivityTransitionKind.Cancel)] },
            "wait" => activity with { BookmarkIds = ["saved-wait"] },
            "private-state" => activity with { PrivateState = NativeFixture.Activity().PrivateState },
            "contract" => activity with { ContractIdentity = new(activity.ContractIdentity!.ActivityTypeKey,
                activity.ContractIdentity.ContractVersion, new('f', 64)) },
            "nested" => activity with { ParentActivityExecutionId = "parent-activity" },
            _ => activity
        };
        Assert.False(JobsNativePreWaitCancellation.Matches(parent, activity));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("partition")]
    [InlineData("material")]
    [InlineData("model-origin")]
    public async Task The_receipt_cannot_move_to_another_tenant_material_or_model_owner(string changed)
    {
        var (parent, _, commit) = Cancellation(true);
        var saved = await new JobsCheckpointEnricher(new ActivityStore()).EnrichAsync(commit);
        var marked = Assert.Single(saved.StateChanges.ActivityExecutions).State;
        parent = changed switch
        {
            "tenant" => parent with { TenantId = "another-tenant" },
            "partition" => parent with { Partition = new("another-partition") },
            "material" => parent with { PinnedExecutable = parent.PinnedExecutable with { ArtifactHash = new('e', 64) } },
            _ => parent with { ModelWorkOrigin = null }
        };
        Assert.False(JobsNativePreWaitCancellation.Matches(parent, marked));
    }

    [Fact]
    public async Task Final_validation_refuses_a_jobs_handoff_added_beside_the_zero_handoff_receipt()
    {
        var (_, _, commit) = Cancellation(true); var enricher = new JobsCheckpointEnricher(new ActivityStore());
        var marked = await enricher.EnrichAsync(commit);
        var (_, _, start) = await NativeFixture.SavedAsync();
        await Assert.ThrowsAsync<JobException>(() => enricher.ValidateFinalAsync(marked with { PostCommitIntents = [start] }).AsTask());
    }

    private static (WorkflowExecutionState Parent, ActivityExecutionState Activity, RuntimeCheckpointCommit Commit) Cancellation(bool modelWorker)
    {
        var original = NativeFixture.Activity();
        var origin = modelWorker ? JobsNativeBoundary.ReadState(NativeFixture.WithModelOwner(original)).Owner.Origin : null;
        var parent = NativeFixture.Parent(WorkflowExecutionStatus.Cancelled) with { ModelWorkOrigin = origin };
        var metadata = original.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim] = "claimed-initial-attempt";
        metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = "initial-invoke-work";
        var activity = original with { Status = ActivityExecutionStatus.Cancelled, CompletedAt = parent.CompletedAt, PrivateState = null,
            BookmarkIds = [], TriggerRegistrations = [], TriggerDeliveries = [], Metadata = metadata,
            Attempts = [new("claimed-initial-attempt", original.InvocationId, 1, ActivityAttemptReason.Initial, NativeFixture.Now)] };
        var source = new Dictionary<string, string> { [RuntimeMetadataKeys.SchedulerWorkItemId] = "cancel-work",
            [RuntimeMetadataKeys.CommandKind] = nameof(WorkflowExecutionCommandKind.Cancel) };
        var checkpointSource = new Dictionary<string, string> { [RuntimeMetadataKeys.SchedulerWorkItemId] = "cancel-work",
            [RuntimeMetadataKeys.CommandId] = "cancel-command", [RuntimeMetadataKeys.CheckpointReason] = "WorkflowCancellation" };
        var commit = NativeFixture.Commit(RuntimeCheckpointNames.ActivityCancelled, activity, parent: parent) with
        {
            CommitId = "commit:cancel-work:cancel", Metadata = source,
            Checkpoint = new("checkpoint:cancel-work:cancel", RuntimeCheckpointNames.ActivityCancelled, parent.WorkflowExecutionId,
                parent.CompletedAt!.Value, [activity.InvocationId], checkpointSource)
        };
        return (parent, activity, commit);
    }
}
