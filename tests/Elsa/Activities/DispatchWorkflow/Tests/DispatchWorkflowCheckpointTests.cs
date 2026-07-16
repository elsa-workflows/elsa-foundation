using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowCheckpointTests
{
    [Fact]
    public async Task Wait_mode_atomically_commits_suspension_bookmark_dispatch_and_start_responsibility()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "wait-atomic",
            parentWorkflowExecutionId: "parent-wait-atomic",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);

        var commit = run.CompletionCommit;
        Assert.Equal(RuntimeCheckpointNames.BookmarkCreated, commit.Checkpoint.Name);
        Assert.Equal(RuntimeMetadataKeys.CheckpointRequirementMandatory, commit.Metadata[RuntimeMetadataKeys.CheckpointRequirement]);
        var activity = Assert.Single(commit.StateChanges.ActivityExecutions).State;
        Assert.Equal(ActivityExecutionStatus.Suspended, activity.Status);
        Assert.Equal(BookmarkSuspension.SuspendedSubStatus, activity.SubStatus);
        Assert.Equal([run.Identity.WaitBookmarkId], activity.BookmarkIds);

        var bookmark = Assert.Single(commit.StateChanges.Bookmarks).State;
        Assert.Equal(run.Identity.WaitBookmarkId, bookmark.BookmarkId);
        Assert.Equal(run.Identity.WaitStimulusHash, bookmark.StimulusHash);
        Assert.Equal(DispatchWorkflowConstants.WaitStimulusType, bookmark.StimulusType);
        Assert.Equal(DispatchWorkflowConstants.CompletionResumeTargetId, bookmark.ResumeTargetId);
        Assert.Null(bookmark.ExpiresAt);
        Assert.Equal(run.Identity.WaitBookmarkId, (await fixture.FindBookmarkAsync(run.Start.WorkflowExecutionId, run.Identity.WaitBookmarkId))?.BookmarkId);

        var dispatch = Assert.Single(commit.StateChanges.WorkflowDispatches).State;
        Assert.Equal(WorkflowDispatchMode.WaitForCompletion, dispatch.Mode);
        Assert.Equal(WorkflowDispatchStatus.Pending, dispatch.Status);
        var startIntent = Assert.Single(commit.PostCommitIntents);
        Assert.Equal(run.Identity.StartIntentId, startIntent.IntentId);
        Assert.Equal(DispatchWorkflowConstants.StartChildIntentKind, startIntent.Kind);
        Assert.Single(commit.StateChanges.PostCommitOutbox);

        var childIdOutput = Assert.Single(
            commit.StateChanges.DurableValues,
            change => change.State.ValueId == "dispatch-child-id-wait-atomic");
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, childIdOutput.State.InlineValue?.GetString());
        var inspection = Assert.Single(commit.StateChanges.ActivityExecutionInspections).State;
        Assert.Empty(inspection.OutcomeNames);
        Assert.Single(
            inspection.ValueSnapshots,
            snapshot => snapshot.Subject == ActivityExecutionInspectionValueSubject.ActivityOutput &&
                        snapshot.Name == nameof(Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow.ChildWorkflowExecutionId));
        Assert.Equal(ActivityExecutionStatus.Suspended, run.Activity.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        Assert.Empty(fixture.ChildProbe.Observations);
        Assert.DoesNotContain(fixture.Actors.Envelopes, envelope => envelope.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId);
        Assert.Equal(run.Identity.WaitBookmarkId, Assert.Single(fixture.BookmarkObserver.Created).BookmarkId);
    }

    [Fact]
    public async Task Wait_checkpoint_replay_is_equivalent_and_conflicting_replay_fails_closed()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "wait-replay",
            parentWorkflowExecutionId: "parent-wait-replay",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);

        var replay = await fixture.ReplayAsync(run.CompletionCommit);
        Assert.Single(replay.PendingPostCommitWorkIds);
        Assert.Single(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId));
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));

        var conflict = run.CompletionCommit with
        {
            Metadata = run.CompletionCommit.Metadata
                .Concat(new[] { new KeyValuePair<string, string>("test.conflict", "true") })
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
        };
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => fixture.ReplayAsync(conflict).AsTask());
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
    }

    [Fact]
    public async Task Wait_activity_reexecution_uses_durable_invoke_time_after_wall_clock_advances()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "wait-reexecute-time",
            parentWorkflowExecutionId: "parent-wait-reexecute-time",
            parentCorrelationId: "correlation-parent",
            waitForCompletion: true);

        var first = await ExecuteActivityAgainAsync(fixture, run, waitForCompletion: true);
        fixture.AdvanceTime(TimeSpan.FromHours(6));
        var second = await ExecuteActivityAgainAsync(fixture, run, waitForCompletion: true);

        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(first.Record, second.Record));
        Assert.Equal(run.CompletionCommit.Checkpoint.OccurredAt, second.Record.CreatedAt);
        Assert.Equal(first.StartIntent.RecordedAt, second.StartIntent.RecordedAt);
        Assert.Equal(first.StartIntent.IntentId, second.StartIntent.IntentId);
        Assert.Equal(first.StartIntent.IdempotencyKey, second.StartIntent.IdempotencyKey);
        Assert.Equal(first.StartIntent.Payload?.GetRawText(), second.StartIntent.Payload?.GetRawText());
        Assert.NotNull(first.WaitBookmark);
        Assert.NotNull(second.WaitBookmark);
        Assert.Equal(first.WaitBookmark.BookmarkId, second.WaitBookmark.BookmarkId);
        Assert.Equal(first.WaitBookmark.ResumeTargetId, second.WaitBookmark.ResumeTargetId);
        Assert.Equal(first.WaitBookmark.StimulusType, second.WaitBookmark.StimulusType);
        Assert.Equal(first.WaitBookmark.StimulusHash, second.WaitBookmark.StimulusHash);
        Assert.Equal(first.WaitBookmark.ExpiresAt, second.WaitBookmark.ExpiresAt);
        Assert.Equal(first.WaitBookmark.Metadata, second.WaitBookmark.Metadata);

        var originalDispatchChange = Assert.Single(run.CompletionCommit.StateChanges.WorkflowDispatches);
        var reconstructedCommit = run.CompletionCommit with
        {
            StateChanges = run.CompletionCommit.StateChanges.WithWorkflowDispatches(
            [
                originalDispatchChange with { State = second.Record }
            ]),
            PostCommitIntents = [second.StartIntent]
        };
        Assert.Equal(
            JsonSerializer.Serialize(Assert.Single(run.CompletionCommit.StateChanges.WorkflowDispatches).State),
            JsonSerializer.Serialize(second.Record));
        Assert.Equal(
            JsonSerializer.Serialize(Assert.Single(run.CompletionCommit.PostCommitIntents)),
            JsonSerializer.Serialize(second.StartIntent));
        Assert.Equal(
            RuntimeCheckpointCommitFingerprint.Compute(run.CompletionCommit),
            RuntimeCheckpointCommitFingerprint.Compute(reconstructedCommit));
        await fixture.ReplayAsync(reconstructedCommit);
    }

    [Fact]
    public async Task FireAndForget_reexecution_retains_wall_clock_timestamp_semantics()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync(
            caseId: "fire-and-forget-time",
            parentWorkflowExecutionId: "parent-fire-and-forget-time",
            parentCorrelationId: "correlation-parent");

        var first = await ExecuteActivityAgainAsync(fixture, run, waitForCompletion: false);
        fixture.AdvanceTime(TimeSpan.FromHours(6));
        var second = await ExecuteActivityAgainAsync(fixture, run, waitForCompletion: false);

        Assert.Equal(DispatchWorkflowRuntimeTestFixture.Now, first.Record.CreatedAt);
        Assert.Equal(DispatchWorkflowRuntimeTestFixture.Now.AddHours(6), second.Record.CreatedAt);
        Assert.Equal(second.Record.CreatedAt, second.StartIntent.RecordedAt);
        Assert.Null(first.WaitBookmark);
        Assert.Null(second.WaitBookmark);
    }

    [Fact]
    public async Task Failed_wait_checkpoint_exposes_no_dispatch_bookmark_or_child()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(failDispatchCommit: true);
        const string parentId = "parent-wait-rollback";

        var failedStart = await fixture.StartParentExpectingFailureAsync(
            caseId: "wait-rollback",
            parentWorkflowExecutionId: parentId,
            waitForCompletion: true);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, failedStart.CommandDispatch.Status);

        var activity = Assert.Single(await fixture.ListActivitiesAsync(parentId));
        var identity = new WorkflowDispatchIdentity(parentId, activity.Execution.ActivityExecutionId);
        Assert.Empty(await fixture.ListDispatchesAsync(parentId));
        Assert.Null(await fixture.FindBookmarkAsync(parentId, identity.WaitBookmarkId));
        Assert.Null(await fixture.FindWorkflowAsync(identity.ChildWorkflowExecutionId));
        Assert.Empty(fixture.BookmarkObserver.Created);
        Assert.DoesNotContain(fixture.Actors.Envelopes, envelope => envelope.WorkflowExecutionId == identity.ChildWorkflowExecutionId);
    }

    [Fact]
    public async Task Activity_completion_atomically_commits_dispatch_responsibility_before_global_delivery()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();

        var run = await fixture.StartParentAsync(
            caseId: "checkpoint-atomic",
            parentWorkflowExecutionId: "parent-checkpoint-atomic",
            parentCorrelationId: "correlation-parent");

        var commit = run.CompletionCommit;
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, commit.Checkpoint.Name);
        var activityChange = Assert.Single(commit.StateChanges.ActivityExecutions);
        Assert.Equal(ActivityExecutionStatus.Completed, activityChange.State.Status);
        var inspectionChange = Assert.Single(commit.StateChanges.ActivityExecutionInspections);
        Assert.Equal([DispatchWorkflowOutcomes.Dispatched], inspectionChange.State.OutcomeNames);

        var childIdOutput = Assert.Single(
            commit.StateChanges.DurableValues,
            change => change.State.ValueId == "dispatch-child-id-checkpoint-atomic");
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, childIdOutput.State.InlineValue?.GetString());

        var dispatchChange = Assert.Single(commit.StateChanges.WorkflowDispatches);
        Assert.Equal(run.Identity.DispatchId, dispatchChange.StateId);
        Assert.Equal(WorkflowDispatchStatus.Pending, dispatchChange.State.Status);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, dispatchChange.State.ChildWorkflowExecutionId);

        Assert.Equal(2, commit.PostCommitIntents.Count);
        var continuation = Assert.Single(
            commit.PostCommitIntents,
            intent => intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork);
        var continuationWork = continuation.Payload?.Deserialize<RuntimeSchedulerWorkItem>();
        Assert.NotNull(continuationWork);
        Assert.Equal(WorkflowExecutionCommandKind.CompleteActivity, continuationWork.CommandKind);
        Assert.Single(
            commit.PostCommitIntents,
            intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);

        Assert.Equal(2, commit.StateChanges.PostCommitOutbox.Count);
        Assert.All(
            commit.StateChanges.PostCommitOutbox,
            change => Assert.Equal(RuntimePostCommitOutboxStatus.Pending, change.State.Status));
        Assert.Equal(
            new[] { RuntimePostCommitIntentKinds.EnqueueSchedulerWork, DispatchWorkflowConstants.StartChildIntentKind }.Order(StringComparer.Ordinal),
            commit.StateChanges.PostCommitOutbox.Select(change => change.State.Intent.Kind).Order(StringComparer.Ordinal));

        Assert.Equal(ActivityExecutionStatus.Completed, run.Activity.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))?.Status);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        Assert.Empty(fixture.ChildProbe.Observations);
    }

    [Fact]
    public async Task Equivalent_checkpoint_replay_converges_on_one_dispatch_child_and_start_intent()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        const string parentWorkflowExecutionId = "parent-checkpoint-replay";
        var run = await fixture.StartParentAsync(
            caseId: "checkpoint-replay",
            parentWorkflowExecutionId: parentWorkflowExecutionId,
            parentCorrelationId: "correlation-parent");

        await fixture.ReplayAsync(run.CompletionCommit);

        var persisted = Assert.Single(await fixture.ListDispatchesAsync(parentWorkflowExecutionId));
        Assert.Equal(run.Identity.DispatchId, persisted.DispatchId);
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, persisted.ChildWorkflowExecutionId);
        Assert.Single(
            run.CompletionCommit.PostCommitIntents,
            intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);
        Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
        Assert.Empty(fixture.ChildProbe.Observations);
    }

    [Fact]
    public async Task Distinct_activity_executions_receive_distinct_dispatch_and_child_identities()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();

        var first = await fixture.StartParentAsync(
            caseId: "checkpoint-first",
            parentWorkflowExecutionId: "parent-checkpoint-first",
            parentCorrelationId: "correlation-parent");
        var second = await fixture.StartParentAsync(
            caseId: "checkpoint-second",
            parentWorkflowExecutionId: "parent-checkpoint-second",
            parentCorrelationId: "correlation-parent");

        Assert.NotEqual(first.Activity.Execution.ActivityExecutionId, second.Activity.Execution.ActivityExecutionId);
        Assert.NotEqual(first.Identity.DispatchId, second.Identity.DispatchId);
        Assert.NotEqual(first.Identity.ChildWorkflowExecutionId, second.Identity.ChildWorkflowExecutionId);
        Assert.NotEqual(first.Identity.StartIntentId, second.Identity.StartIntentId);
        Assert.Equal(first.Identity.DispatchId, Assert.Single(
            await fixture.ListDispatchesAsync(first.Start.WorkflowExecutionId)).DispatchId);
        Assert.Equal(second.Identity.DispatchId, Assert.Single(
            await fixture.ListDispatchesAsync(second.Start.WorkflowExecutionId)).DispatchId);
    }

    private static async ValueTask<WorkflowDispatchCheckpointRequest> ExecuteActivityAgainAsync(
        DispatchWorkflowRuntimeTestFixture fixture,
        ParentDispatchRun run,
        bool waitForCompletion)
    {
        var commit = run.CompletionCommit;
        var artifactId = commit.Metadata[RuntimeMetadataKeys.ExecutableArtifactId];
        var executable = await fixture.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(artifactId)
            ?? throw new InvalidOperationException($"Executable '{artifactId}' was not found.");
        var activity = new DispatchWorkflowActivity
        {
            WorkflowDefinitionId = new InputArgument<string>(new Variable("definition")),
            Inputs = new InputArgument<IReadOnlyDictionary<string, object?>>(new Variable("inputs")),
            WaitForCompletion = new InputArgument<bool>(new Variable("wait"))
        };
        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: commit.Metadata[RuntimeMetadataKeys.SchedulerWorkItemId],
            workflowExecutionId: run.Start.WorkflowExecutionId,
            commandId: commit.Metadata[RuntimeMetadataKeys.CommandId],
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope:wait-reexecute-time",
            idempotencyKey: "invoke:wait-reexecute-time",
            enqueuedAt: commit.Checkpoint.OccurredAt,
            recordedAt: commit.Checkpoint.OccurredAt);
        var context = new SimpleActivityExecutionContext(
            fixture.Services,
            activity,
            CancellationToken.None,
            run.Start.WorkflowExecutionId,
            executable.Identity,
            workItem,
            executable.RootActivity,
            run.Activity);
        context.Set(activity.WorkflowDefinitionId.MemoryBlockReference(), DispatchWorkflowRuntimeTestFixture.ChildIdentity.DefinitionId);
        context.Set(
            activity.Inputs.MemoryBlockReference(),
            new Dictionary<string, object?>
            {
                ["message"] = JsonSerializer.SerializeToElement("hello child"),
                ["count"] = JsonSerializer.SerializeToElement(7),
                ["tenant"] = JsonSerializer.SerializeToElement("workflow-input-tenant")
            });
        context.Set(activity.WaitForCompletion.MemoryBlockReference(), waitForCompletion);

        await ((IActivity)activity).ExecuteAsync(context);

        return context.WorkflowDispatchRequest
            ?? throw new InvalidOperationException("DispatchWorkflow did not stage a checkpoint request.");
    }
}
