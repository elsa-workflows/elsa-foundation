using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCheckpointCommitTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly WorkflowExecutableIdentity _executableIdentity;
    private readonly WorkflowExecutionState _workflowState;
    private readonly ActivityExecutionState _activityState;
    private readonly SchedulerState _schedulerState;
    private readonly DurableValueState _durableValueState;
    private readonly IncidentState _incidentState;
    private readonly ExecutionLivenessState _operationalState;

    public RuntimeCheckpointCommitTests()
    {
        _executableIdentity = new WorkflowExecutableIdentity(
            ArtifactId: "artifact-1",
            DefinitionId: "definition-1",
            DefinitionVersionId: "definition-version-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:artifact",
            Source: new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", "definition-version-1", "1.0.0"));
        _workflowState = new WorkflowExecutionState(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: _executableIdentity,
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: null,
            CorrelationId: "order-123",
            ParentWorkflowExecutionId: null,
            TenantId: "tenant-a",
            SystemMetadata: new Dictionary<string, string>());
        _activityState = new ActivityExecutionState(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-1",
                AuthoredActivityId: "activity-authored-1",
                ActivityType: "Elsa.SendEmail",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: _now,
            StartedAt: _now,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: "branch-1",
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(
                "wfexec-1",
                parentActivityExecutionId: null,
                schedulingActivityExecutionId: null,
                branchId: "branch-1",
                iterationId: null,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: "test"),
            CallStackDepth: 0,
            BookmarkIds: ["bookmark-1"],
            IncidentIds: ["incident-1"],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());
        _schedulerState = new SchedulerState(
            workflowExecutionId: "wfexec-1",
            version: 1,
            pendingWork: [],
            pendingContinuations: [],
            volatileWaits: []);
        _durableValueState = new DurableValueState(
            durableValueId: "durable-1",
            workflowExecutionId: "wfexec-1",
            valueId: "customer",
            type: new RuntimeValueTypeDescriptor("reference", "crm.customer", null),
            lifecycle: DurableValueLifecycle.Instance,
            storage: DurableValueStorage.Inline,
            inlineValue: Json("""{"id":"customer-1"}"""),
            externalReference: null,
            sourceActivityExecutionId: "actexec-1",
            capturedAt: _now,
            metadata: new Dictionary<string, string>());
        _incidentState = new IncidentState(
            incidentId: "incident-1",
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            executableNodeId: "node-1",
            severity: IncidentSeverity.Error,
            status: IncidentStatus.Blocking,
            resolutionAction: IncidentResolutionAction.FaultWorkflow,
            failureType: "ActivityFaulted",
            message: "Activity failed.",
            createdAt: _now,
            resolvedAt: null,
            metadata: new Dictionary<string, string>());
        _operationalState = new ExecutionLivenessState(
            operationalStateId: "operational-1",
            workflowExecutionId: "wfexec-1",
            executionLease: new RuntimeExecutionLease(
                leaseId: "lease-1",
                workflowExecutionId: "wfexec-1",
                ownerId: "worker-1",
                acquiredAt: _now,
                expiresAt: _now.AddMinutes(5),
                fencingToken: 1),
            heartbeat: new RuntimeHeartbeat(
                heartbeatId: "heartbeat-1",
                workflowExecutionId: "wfexec-1",
                ownerId: "worker-1",
                leaseId: "lease-1",
                recordedAt: _now),
            drain: null,
            interruptedExecution: null,
            pendingPostCommitIntentIds: ["intent-1"],
            metadata: new Dictionary<string, string>());
    }

    [Theory]
    [InlineData(RuntimeCheckpointNames.WorkflowStarted)]
    [InlineData(RuntimeCheckpointNames.ActivityScheduled)]
    [InlineData(RuntimeCheckpointNames.ActivityStarted)]
    [InlineData(RuntimeCheckpointNames.ActivityCompleted)]
    [InlineData(RuntimeCheckpointNames.WorkflowSuspended)]
    [InlineData(RuntimeCheckpointNames.WorkflowCompleted)]
    [InlineData(RuntimeCheckpointNames.IncidentRecorded)]
    public void RuntimeCheckpointCommit_CarriesAtomicRuntimeStateChanges(string checkpointName)
    {
        var commit = NewCommit(checkpointName);

        Assert.Equal(checkpointName, commit.Checkpoint.Name);
        Assert.Equal("wfexec-1", commit.WorkflowExecutionId);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, commit.StateChanges.WorkflowExecution!.Operation);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, commit.StateChanges.Scheduler!.Operation);
        Assert.Single(commit.StateChanges.ActivityExecutions);
        Assert.Single(commit.StateChanges.DurableValues);
        Assert.Equal("node-resume-1", Assert.Single(commit.StateChanges.Bookmarks).State.ResumeTargetId);
        Assert.True(Assert.Single(commit.StateChanges.Incidents).State.IsBlocking);
        Assert.Equal("lease-1", Assert.Single(commit.StateChanges.Operational).State.ExecutionLease!.LeaseId);
    }

    [Fact]
    public async Task CheckpointCommitter_UsesPolicyDecisionWithoutChangingCheckpointSemantics()
    {
        var commit = NewCommit(RuntimeCheckpointNames.ActivityCompleted);
        var immediateStore = new RecordingCommitStore();
        var deferredStore = new RecordingCommitStore();

        await NewCommitter(RuntimeCheckpointPersistenceMode.Immediate, immediateStore).CommitAsync(commit);
        await NewCommitter(RuntimeCheckpointPersistenceMode.Deferred, deferredStore).CommitAsync(commit);

        var immediateCommit = Assert.Single(immediateStore.Commits);
        var deferredCommit = Assert.Single(deferredStore.Commits);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, immediateCommit.Decision.Mode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, deferredCommit.Decision.Mode);
        Assert.Equal(immediateCommit.Commit.Checkpoint, deferredCommit.Commit.Checkpoint);
        Assert.Equal(immediateCommit.Commit.StateChanges.WorkflowExecution!.State, deferredCommit.Commit.StateChanges.WorkflowExecution!.State);
        Assert.Equal(immediateCommit.Commit.StateChanges.Scheduler!.State, deferredCommit.Commit.StateChanges.Scheduler!.State);
        Assert.Equal(
            immediateCommit.Commit.StateChanges.ActivityExecutions.Select(change => change.StateId),
            deferredCommit.Commit.StateChanges.ActivityExecutions.Select(change => change.StateId));
        Assert.Equal(
            immediateCommit.Commit.StateChanges.DurableValues.Select(change => change.StateId),
            deferredCommit.Commit.StateChanges.DurableValues.Select(change => change.StateId));
        Assert.Equal(
            immediateCommit.Commit.StateChanges.Bookmarks.Select(change => change.State.ResumeTargetId),
            deferredCommit.Commit.StateChanges.Bookmarks.Select(change => change.State.ResumeTargetId));
    }

    [Fact]
    public async Task CheckpointCommitter_ReturnsCommitResultWithPendingWorkIdsWithoutInlineDelivery()
    {
        var events = new List<string>();
        var commitStore = new RecordingCommitStore(events, pendingPostCommitWorkIds: ["commit-1:intent-1", "commit-1:intent-2"]);
        var commit = NewCommit(
            RuntimeCheckpointNames.PostCommitIntentRecorded,
            [
                NewIntent("intent-1"),
                NewIntent("intent-2")
            ]);

        var result = await NewCommitter(RuntimeCheckpointPersistenceMode.Immediate, commitStore).CommitAsync(commit);

        Assert.True(result.Succeeded);
        Assert.Equal("commit-1", result.CommitId);
        Assert.Equal("wfexec-1", result.WorkflowExecutionId);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, result.PersistenceDecision.Mode);
        Assert.Equal(["commit-1:intent-1", "commit-1:intent-2"], result.PendingPostCommitWorkIds);
        Assert.Equal(["commit:commit-1"], events);
        // The committer folds the post-commit intents into the applied change set before handing the commit to the store.
        var persisted = Assert.Single(commitStore.Commits).Commit;
        Assert.Equal(commit.CommitId, persisted.CommitId);
        Assert.Equal(
            ["commit-1:intent-1", "commit-1:intent-2"],
            persisted.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId));
    }

    [Fact]
    public async Task CheckpointCommitter_PropagatesProviderCommitFailures()
    {
        var events = new List<string>();
        var commitStore = new RecordingCommitStore(events, new InvalidOperationException("checkpoint commit failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NewCommitter(RuntimeCheckpointPersistenceMode.Immediate, commitStore).CommitAsync(NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded)));

        Assert.Equal(["commit:commit-1"], events);
    }

    [Fact]
    public async Task CheckpointCommitter_DoesNotCommitWhenPolicySkipsPersistenceWithoutPostCommitWork()
    {
        var commitStore = new RecordingCommitStore();
        var result = await NewCommitter(RuntimeCheckpointPersistenceMode.Skip, commitStore)
            .CommitAsync(NewCommit(RuntimeCheckpointNames.WorkflowCompleted, []));

        Assert.True(result.Succeeded);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Skip, result.PersistenceDecision.Mode);
        Assert.Empty(result.PendingPostCommitWorkIds);
        Assert.Empty(commitStore.Commits);
    }

    [Fact]
    public async Task CheckpointCommitter_ReturnsFailureWhenPolicySkipsCommitWithPostCommitWork()
    {
        var commitStore = new RecordingCommitStore();
        var result = await NewCommitter(RuntimeCheckpointPersistenceMode.Skip, commitStore)
            .CommitAsync(NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded));

        Assert.False(result.Succeeded);
        Assert.Equal(RuntimeCheckpointCommitFailureCodes.SkipHasPostCommitWork, result.FailureCode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Skip, result.PersistenceDecision.Mode);
        Assert.Empty(result.PendingPostCommitWorkIds);
        Assert.Empty(commitStore.Commits);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_IsIdempotentByCommitId()
    {
        var writer = new InMemoryRuntimeCheckpointCommitStore();
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.ActivityStarted);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.ActivityCompleted);

        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(conflictingReplay, decision);

        var write = Assert.Single(writer.ListCommits());
        Assert.Equal("commit-1", write.Commit.CommitId);
        Assert.Equal(RuntimeCheckpointNames.ActivityStarted, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_ProjectsWorkflowExecutionStateChanges()
    {
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(workflowStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var running = NewCommit(RuntimeCheckpointNames.WorkflowStarted);
        var completed = NewCommit(RuntimeCheckpointNames.WorkflowCompleted) with
        {
            CommitId = "commit-2",
            StateChanges = NewStateChanges(workflowState: _workflowState with
            {
                Status = WorkflowExecutionStatus.Completed,
                UpdatedAt = _now.AddMinutes(5),
                CompletedAt = _now.AddMinutes(5)
            })
        };

        await writer.CommitAsync(running, decision);
        await writer.CommitAsync(completed, decision);

        var state = await workflowStateStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(5), state.CompletedAt);
        Assert.Equal(2, writer.ListCommits().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotProjectConflictingReplay()
    {
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(workflowStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.WorkflowStarted);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.WorkflowCompleted) with
        {
            StateChanges = NewStateChanges(
                workflowStateChange: new RuntimeStateChange<WorkflowExecutionState>(
                    StateId: _workflowState.WorkflowExecutionId,
                    Operation: RuntimeStateChangeOperation.Delete,
                    State: _workflowState with
                    {
                        Status = WorkflowExecutionStatus.Completed,
                        UpdatedAt = _now.AddMinutes(5),
                        CompletedAt = _now.AddMinutes(5)
                    },
                    Metadata: new Dictionary<string, string>()))
        };

        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(conflictingReplay, decision);

        var state = await workflowStateStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Running, state.Status);
        Assert.Null(state.CompletedAt);
        var write = Assert.Single(writer.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsUnsupportedWorkflowStateProjectionBeforeRecordingWrite()
    {
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(workflowStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.WorkflowCompleted) with
        {
            StateChanges = NewStateChanges(
                workflowStateChange: new RuntimeStateChange<WorkflowExecutionState>(
                    StateId: _workflowState.WorkflowExecutionId,
                    Operation: RuntimeStateChangeOperation.Delete,
                    State: _workflowState,
                    Metadata: new Dictionary<string, string>()))
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await workflowStateStore.ListAsync());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotRecordWhenWorkflowStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointCommitStore(new ThrowingWorkflowExecutionStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.WorkflowStarted);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotRecordWhenActivityStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointCommitStore(activityExecutionStateStore: new ThrowingActivityExecutionStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.ActivityCompleted);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotRecordWhenBookmarkStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointCommitStore(bookmarkStateStore: new ThrowingBookmarkStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.BookmarkCreated);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotRecordWhenDurableValueStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, new ThrowingDurableValueStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.DurableValueCaptured);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotRecordWhenIncidentStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, new ThrowingIncidentStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.IncidentRecorded);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotRecordWhenOperationalStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, new ThrowingExecutionLivenessStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotRecordWhenSchedulerStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, null, new ThrowingSchedulerStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.ActivityScheduled);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_ProjectsSchedulerStateChanges()
    {
        var schedulerStateStore = new InMemorySchedulerStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, null, schedulerStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var initial = NewCommit(RuntimeCheckpointNames.ActivityScheduled);
        var updatedSchedulerState = _schedulerState with
        {
            Version = 2,
            PendingWork =
            [
                new ScheduledActivityWorkItem(
                    WorkItemId: "work-2",
                    WorkflowExecutionId: "wfexec-1",
                    ExecutableNodeId: "node-2",
                    ActivityExecutionId: null,
                    SchedulingActivityExecutionId: "actexec-1",
                    BranchId: "branch-1",
                    IterationId: null,
                    EnqueuedAt: _now.AddMinutes(1),
                    Reason: "test")
            ]
        };
        var updated = NewCommit(RuntimeCheckpointNames.ActivityScheduled) with
        {
            CommitId = "commit-2",
            StateChanges = NewStateChanges(schedulerState: updatedSchedulerState)
        };

        await writer.CommitAsync(initial, decision);
        await writer.CommitAsync(updated, decision);

        var state = await schedulerStateStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(2, state.Version);
        Assert.Equal("node-2", Assert.Single(state.PendingWork).ExecutableNodeId);
        Assert.Equal(2, writer.ListCommits().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsUnsupportedSchedulerStateProjectionBeforeRecordingWrite()
    {
        var schedulerStateStore = new InMemorySchedulerStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, null, schedulerStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.ActivityScheduled) with
        {
            StateChanges = NewStateChanges(
                schedulerStateChange: new RuntimeStateChange<SchedulerState>(
                    StateId: _schedulerState.WorkflowExecutionId,
                    Operation: RuntimeStateChangeOperation.Delete,
                    State: _schedulerState,
                    Metadata: new Dictionary<string, string>()))
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await schedulerStateStore.ListAsync());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsSchedulerStateFromDifferentWorkflowBeforeRecordingWrite()
    {
        var schedulerStateStore = new InMemorySchedulerStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, null, schedulerStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var schedulerState = _schedulerState with { WorkflowExecutionId = "wfexec-2" };
        var commit = NewCommit(RuntimeCheckpointNames.ActivityScheduled) with
        {
            StateChanges = NewStateChanges(schedulerState: schedulerState)
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("WorkflowExecutionId", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await schedulerStateStore.ListAsync());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotProjectConflictingSchedulerReplay()
    {
        var schedulerStateStore = new InMemorySchedulerStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, null, schedulerStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.ActivityScheduled);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.ActivityScheduled) with
        {
            StateChanges = NewStateChanges(schedulerState: _schedulerState with { Version = 2 })
        };

        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(conflictingReplay, decision);

        var state = await schedulerStateStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(1, state.Version);
        var write = Assert.Single(writer.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityScheduled, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_ProjectsActivityExecutionStateChanges()
    {
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(activityExecutionStateStore: activityStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var running = NewCommit(RuntimeCheckpointNames.ActivityStarted);
        var completed = NewCommit(RuntimeCheckpointNames.ActivityCompleted) with
        {
            CommitId = "commit-2",
            StateChanges = NewStateChanges(activityState: _activityState with
            {
                Status = ActivityExecutionStatus.Completed,
                CompletedAt = _now.AddMinutes(5)
            })
        };

        await writer.CommitAsync(running, decision);
        await writer.CommitAsync(completed, decision);

        var state = await activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(5), state.CompletedAt);
        Assert.Equal(2, writer.ListCommits().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsUnsupportedActivityStateProjectionBeforeRecordingWrite()
    {
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(activityExecutionStateStore: activityStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.ActivityCompleted) with
        {
            StateChanges = NewStateChanges(
                activityStateChange: new RuntimeStateChange<ActivityExecutionState>(
                    StateId: _activityState.Execution.ActivityExecutionId,
                    Operation: RuntimeStateChangeOperation.Delete,
                    State: _activityState,
                    Metadata: new Dictionary<string, string>()))
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await activityStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotProjectConflictingActivityReplay()
    {
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(activityExecutionStateStore: activityStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.ActivityStarted);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.ActivityCompleted) with
        {
            StateChanges = NewStateChanges(
                activityStateChange: new RuntimeStateChange<ActivityExecutionState>(
                    StateId: _activityState.Execution.ActivityExecutionId,
                    Operation: RuntimeStateChangeOperation.Delete,
                    State: _activityState with
                    {
                        Status = ActivityExecutionStatus.Completed,
                        CompletedAt = _now.AddMinutes(5)
                    },
                    Metadata: new Dictionary<string, string>()))
        };

        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(conflictingReplay, decision);

        var state = await activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Null(state.CompletedAt);
        var write = Assert.Single(writer.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityStarted, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_ProjectsBookmarkStateChanges()
    {
        var bookmarkStateStore = new InMemoryBookmarkStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(bookmarkStateStore: bookmarkStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var created = NewCommit(RuntimeCheckpointNames.BookmarkCreated);
        var consumed = NewCommit(RuntimeCheckpointNames.BookmarkConsumed) with
        {
            CommitId = "commit-2",
            StateChanges = NewStateChanges(bookmarks:
            [
                NewBookmarkChange("bookmark-1", "bookmark-1", RuntimeStateChangeOperation.Delete)
            ])
        };

        await writer.CommitAsync(created, decision);

        var bookmark = await bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);
        Assert.Equal("node-resume-1", bookmark.ResumeTargetId);

        await writer.CommitAsync(consumed, decision);

        Assert.Null(await bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Equal(2, writer.ListCommits().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsUnsupportedBookmarkStateProjectionBeforeRecordingWrite()
    {
        var bookmarkStateStore = new InMemoryBookmarkStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(bookmarkStateStore: bookmarkStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.BookmarkCreated) with
        {
            StateChanges = NewStateChanges(bookmarks:
            [
                NewBookmarkChange("bookmark-1", "bookmark-1", RuntimeStateChangeOperation.Append)
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Contains("Delete", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await bookmarkStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsBookmarkStateFromDifferentWorkflowBeforeRecordingWrite()
    {
        var bookmarkStateStore = new InMemoryBookmarkStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(bookmarkStateStore: bookmarkStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.BookmarkCreated) with
        {
            StateChanges = NewStateChanges(bookmarks:
            [
                NewBookmarkChange("bookmark-1", "bookmark-1", workflowExecutionId: "wfexec-2")
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("WorkflowExecutionId", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await bookmarkStateStore.ListAsync("wfexec-1"));
        Assert.Empty(await bookmarkStateStore.ListAsync("wfexec-2"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotProjectConflictingBookmarkReplay()
    {
        var bookmarkStateStore = new InMemoryBookmarkStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(bookmarkStateStore: bookmarkStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.BookmarkCreated);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.BookmarkConsumed) with
        {
            StateChanges = NewStateChanges(bookmarks:
            [
                NewBookmarkChange("bookmark-1", "bookmark-1", RuntimeStateChangeOperation.Delete)
            ])
        };

        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(conflictingReplay, decision);

        var bookmark = await bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);
        Assert.Equal("node-resume-1", bookmark.ResumeTargetId);
        var write = Assert.Single(writer.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.BookmarkCreated, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_ProjectsDurableValueStateChanges()
    {
        var durableValueStateStore = new InMemoryDurableValueStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, durableValueStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var captured = NewCommit(RuntimeCheckpointNames.DurableValueCaptured);
        var deleted = NewCommit(RuntimeCheckpointNames.WorkflowCompleted) with
        {
            CommitId = "commit-2",
            StateChanges = NewStateChanges(durableValues:
            [
                NewDurableValueChange("durable-1", "durable-1", RuntimeStateChangeOperation.Delete)
            ])
        };

        await writer.CommitAsync(captured, decision);

        var durableValue = await durableValueStateStore.FindAsync("wfexec-1", "durable-1");
        Assert.NotNull(durableValue);
        Assert.Equal("customer", durableValue.ValueId);

        await writer.CommitAsync(deleted, decision);

        Assert.Null(await durableValueStateStore.FindAsync("wfexec-1", "durable-1"));
        Assert.Equal(2, writer.ListCommits().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsUnsupportedDurableValueStateProjectionBeforeRecordingWrite()
    {
        var durableValueStateStore = new InMemoryDurableValueStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, durableValueStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.DurableValueCaptured) with
        {
            StateChanges = NewStateChanges(durableValues:
            [
                NewDurableValueChange("durable-1", "durable-1", RuntimeStateChangeOperation.Append)
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Contains("Delete", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await durableValueStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsDurableValueStateFromDifferentWorkflowBeforeRecordingWrite()
    {
        var durableValueStateStore = new InMemoryDurableValueStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, durableValueStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.DurableValueCaptured) with
        {
            StateChanges = NewStateChanges(durableValues:
            [
                NewDurableValueChange("durable-1", "durable-1", workflowExecutionId: "wfexec-2")
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("WorkflowExecutionId", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await durableValueStateStore.ListAsync("wfexec-1"));
        Assert.Empty(await durableValueStateStore.ListAsync("wfexec-2"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotProjectConflictingDurableValueReplay()
    {
        var durableValueStateStore = new InMemoryDurableValueStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, durableValueStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.DurableValueCaptured);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.WorkflowCompleted) with
        {
            StateChanges = NewStateChanges(durableValues:
            [
                NewDurableValueChange("durable-1", "durable-1", RuntimeStateChangeOperation.Delete)
            ])
        };

        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(conflictingReplay, decision);

        var durableValue = await durableValueStateStore.FindAsync("wfexec-1", "durable-1");
        Assert.NotNull(durableValue);
        Assert.Equal("customer", durableValue.ValueId);
        var write = Assert.Single(writer.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.DurableValueCaptured, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_ProjectsIncidentStateChanges()
    {
        var incidentStateStore = new InMemoryIncidentStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, incidentStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var recorded = NewCommit(RuntimeCheckpointNames.IncidentRecorded);
        var resolved = NewCommit(RuntimeCheckpointNames.IncidentRecorded) with
        {
            CommitId = "commit-2",
            StateChanges = NewStateChanges(incidents:
            [
                NewIncidentChange(
                    "incident-1",
                    "incident-1",
                    RuntimeStateChangeOperation.Upsert,
                    status: IncidentStatus.Resolved)
            ])
        };

        await writer.CommitAsync(recorded, decision);

        var incident = await incidentStateStore.FindAsync("wfexec-1", "incident-1");
        Assert.NotNull(incident);
        Assert.True(incident.IsBlocking);
        Assert.Single(await incidentStateStore.ListBlockingAsync("wfexec-1"));

        await writer.CommitAsync(resolved, decision);

        incident = await incidentStateStore.FindAsync("wfexec-1", "incident-1");
        Assert.NotNull(incident);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Empty(await incidentStateStore.ListBlockingAsync("wfexec-1"));
        Assert.Equal(2, writer.ListCommits().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsUnsupportedIncidentStateProjectionBeforeRecordingWrite()
    {
        var incidentStateStore = new InMemoryIncidentStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, incidentStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.IncidentRecorded) with
        {
            StateChanges = NewStateChanges(incidents:
            [
                NewIncidentChange("incident-1", "incident-1", RuntimeStateChangeOperation.Delete)
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("Append", exception.Message);
        Assert.Contains("Upsert", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await incidentStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsIncidentStateFromDifferentWorkflowBeforeRecordingWrite()
    {
        var incidentStateStore = new InMemoryIncidentStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, incidentStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.IncidentRecorded) with
        {
            StateChanges = NewStateChanges(incidents:
            [
                NewIncidentChange("incident-1", "incident-1", workflowExecutionId: "wfexec-2")
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("WorkflowExecutionId", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await incidentStateStore.ListAsync("wfexec-1"));
        Assert.Empty(await incidentStateStore.ListAsync("wfexec-2"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotProjectConflictingIncidentReplay()
    {
        var incidentStateStore = new InMemoryIncidentStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, incidentStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.IncidentRecorded);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.IncidentRecorded) with
        {
            StateChanges = NewStateChanges(incidents:
            [
                NewIncidentChange(
                    "incident-1",
                    "incident-1",
                    RuntimeStateChangeOperation.Upsert,
                    status: IncidentStatus.Resolved)
            ])
        };

        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(conflictingReplay, decision);

        var incident = await incidentStateStore.FindAsync("wfexec-1", "incident-1");
        Assert.NotNull(incident);
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        var write = Assert.Single(writer.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.IncidentRecorded, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsDuplicateIncidentAppendBeforeRecordingSecondWrite()
    {
        var incidentStateStore = new InMemoryIncidentStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, incidentStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.IncidentRecorded);
        var duplicateAppend = NewCommit(RuntimeCheckpointNames.IncidentRecorded) with
        {
            CommitId = "commit-2",
            StateChanges = NewStateChanges(incidents:
            [
                NewIncidentChange(
                    "incident-1",
                    "incident-1",
                    RuntimeStateChangeOperation.Append,
                    status: IncidentStatus.Blocking)
            ])
        };

        await writer.CommitAsync(first, decision);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(duplicateAppend, decision).AsTask());

        Assert.Contains("already exists", exception.Message);
        Assert.Equal(IncidentStatus.Blocking, (await incidentStateStore.FindAsync("wfexec-1", "incident-1"))!.Status);
        var write = Assert.Single(writer.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.IncidentRecorded, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_ProjectsOperationalStateChanges()
    {
        var operationalStateStore = new InMemoryExecutionLivenessStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, operationalStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var recorded = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded);
        var updated = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded) with
        {
            CommitId = "commit-2",
            StateChanges = NewStateChanges(operational:
            [
                NewOperationalChange("operational-1", "operational-1", ownerId: "worker-2", fencingToken: 2)
            ])
        };

        await writer.CommitAsync(recorded, decision);

        var operationalState = await operationalStateStore.FindAsync("wfexec-1", "operational-1");
        Assert.NotNull(operationalState);
        Assert.Equal("worker-1", operationalState.ExecutionLease!.OwnerId);

        await writer.CommitAsync(updated, decision);

        operationalState = await operationalStateStore.FindAsync("wfexec-1", "operational-1");
        Assert.NotNull(operationalState);
        Assert.Equal("worker-2", operationalState.ExecutionLease!.OwnerId);
        Assert.Equal(2, operationalState.ExecutionLease.FencingToken);
        Assert.Equal(2, writer.ListCommits().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsUnsupportedOperationalStateProjectionBeforeRecordingWrite()
    {
        var operationalStateStore = new InMemoryExecutionLivenessStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, operationalStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded) with
        {
            StateChanges = NewStateChanges(operational:
            [
                NewOperationalChange("operational-1", "operational-1", RuntimeStateChangeOperation.Delete)
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await operationalStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_RejectsOperationalStateFromDifferentWorkflowBeforeRecordingWrite()
    {
        var operationalStateStore = new InMemoryExecutionLivenessStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, operationalStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded) with
        {
            StateChanges = NewStateChanges(operational:
            [
                NewOperationalChange("operational-1", "operational-1", workflowExecutionId: "wfexec-2")
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("WorkflowExecutionId", exception.Message);
        Assert.Empty(writer.ListCommits());
        Assert.Empty(await operationalStateStore.ListAsync("wfexec-1"));
        Assert.Empty(await operationalStateStore.ListAsync("wfexec-2"));
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_DoesNotProjectConflictingOperationalReplay()
    {
        var operationalStateStore = new InMemoryExecutionLivenessStateStore();
        var writer = new InMemoryRuntimeCheckpointCommitStore(null, null, null, null, null, operationalStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded) with
        {
            StateChanges = NewStateChanges(operational:
            [
                NewOperationalChange("operational-1", "operational-1", ownerId: "worker-2", fencingToken: 2)
            ])
        };

        await writer.CommitAsync(first, decision);
        await writer.CommitAsync(conflictingReplay, decision);

        var operationalState = await operationalStateStore.FindAsync("wfexec-1", "operational-1");
        Assert.NotNull(operationalState);
        Assert.Equal("worker-1", operationalState.ExecutionLease!.OwnerId);
        var write = Assert.Single(writer.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.PostCommitIntentRecorded, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public void RuntimeCheckpointStateChangeSet_RejectsMismatchedIncidentStateIds()
    {
        var invalidIncidents = new[]
        {
            new RuntimeStateChange<IncidentState>(
                StateId: "bookmark-1",
                Operation: RuntimeStateChangeOperation.Append,
                State: _incidentState,
                Metadata: new Dictionary<string, string>())
        };

        var exception = Assert.Throws<ArgumentException>(() => NewStateChanges(incidents: invalidIncidents));

        Assert.Contains("IncidentState.IncidentId", exception.Message);
    }

    [Fact]
    public void RuntimeCheckpointStateChangeSet_RejectsMismatchedBookmarkStateIds()
    {
        var invalidBookmarks = new[]
        {
            NewBookmarkChange("bookmark-state-change-id", "bookmark-state-id")
        };

        var exception = Assert.Throws<ArgumentException>(() => NewStateChanges(bookmarks: invalidBookmarks));

        Assert.Contains("StateId", exception.Message);
        Assert.Contains("BookmarkState.BookmarkId", exception.Message);
    }

    [Fact]
    public void RuntimeCheckpointStateChangeSet_RejectsMismatchedDurableValueStateIds()
    {
        var invalidDurableValues = new[]
        {
            NewDurableValueChange("durable-state-change-id", "durable-state-id")
        };

        var exception = Assert.Throws<ArgumentException>(() => NewStateChanges(durableValues: invalidDurableValues));

        Assert.Contains("StateId", exception.Message);
        Assert.Contains("DurableValueState.DurableValueId", exception.Message);
    }

    [Fact]
    public void RuntimeCheckpointStateChangeSet_RejectsMismatchedOperationalStateIds()
    {
        var invalidOperational = new[]
        {
            new RuntimeStateChange<ExecutionLivenessState>(
                StateId: "lease-1",
                Operation: RuntimeStateChangeOperation.Upsert,
                State: _operationalState,
                Metadata: new Dictionary<string, string>())
        };

        var exception = Assert.Throws<ArgumentException>(() => NewStateChanges(operational: invalidOperational));

        Assert.Contains("ExecutionLivenessState.OperationalStateId", exception.Message);
    }


    [Fact]
    public async Task InMemoryCheckpointCommitStore_SurfacesDuplicateOutboxIntentAsValidationError_NotDurabilityFailure()
    {
        // #386: two outbox items with the same OutboxItemId but different intents are a data-conflict
        // validation failure. It must surface as the store's InvalidOperationException — not be re-wrapped
        // as RuntimeCheckpointInconsistentDurabilityException, which describes partial persistence and
        // sends operators down the wrong diagnostic path.
        var writer = new InMemoryRuntimeCheckpointCommitStore();
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded) with
        {
            StateChanges = NewStateChanges().WithPostCommitOutbox(
            [
                NewOutboxChange("outbox-1", "intent-1"),
                NewOutboxChange("outbox-1", "intent-2")
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("already exists with a different intent or status", exception.Message);
        Assert.Empty(writer.ListCommits());
    }

    [Fact]
    public async Task InMemoryCheckpointCommitStore_SurfacesNonPendingOutboxItemAsValidationError_NotDurabilityFailure()
    {
        // #386: a non-pending outbox item in the applied change set is likewise a validation failure,
        // not an inconsistent-durability condition.
        var writer = new InMemoryRuntimeCheckpointCommitStore();
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded) with
        {
            StateChanges = NewStateChanges().WithPostCommitOutbox(
            [
                NewOutboxChange("outbox-1", "intent-1", RuntimePostCommitOutboxStatus.Delivered)
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(commit, decision).AsTask());

        Assert.Contains("pending", exception.Message);
        Assert.Empty(writer.ListCommits());
    }

    private RuntimeStateChange<RuntimePostCommitOutboxItem> NewOutboxChange(
        string outboxItemId,
        string intentId,
        RuntimePostCommitOutboxStatus status = RuntimePostCommitOutboxStatus.Pending) =>
        new(
            StateId: outboxItemId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: new RuntimePostCommitOutboxItem(
                outboxItemId: outboxItemId,
                intent: NewIntent(intentId),
                status: status,
                recordedAt: _now,
                availableAt: _now,
                deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? _now : null),
            Metadata: new Dictionary<string, string>());

    [Fact]
    public void RuntimeCheckpointStateChangeSet_RejectsMismatchedActivityExecutionStateIds()
    {
        // #399: ActivityExecutions was the only collection missing StateId consistency validation; a
        // provider indexing by StateId would silently store the wrong activity-execution record.
        var invalidActivityExecution = new RuntimeStateChange<ActivityExecutionState>(
            StateId: "actexec-other",
            Operation: RuntimeStateChangeOperation.Upsert,
            State: _activityState,
            Metadata: new Dictionary<string, string>());

        var exception = Assert.Throws<ArgumentException>(() => NewStateChanges(activityStateChange: invalidActivityExecution));

        Assert.Contains("StateId", exception.Message);
        Assert.Contains("ActivityExecutionState.Execution.ActivityExecutionId", exception.Message);
    }

    private RuntimeCheckpointCommit NewCommit(
        string checkpointName,
        IReadOnlyList<RuntimePostCommitIntent>? postCommitIntents = null) =>
        new(
            CommitId: "commit-1",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint-1",
                Name: checkpointName,
                WorkflowExecutionId: "wfexec-1",
                OccurredAt: _now,
                ActivityExecutionIds: ["actexec-1"],
                Metadata: new Dictionary<string, string>()),
            StateChanges: NewStateChanges(),
            PostCommitIntents: postCommitIntents ?? [NewIntent("intent-1")],
            Metadata: new Dictionary<string, string>());

    private RuntimePostCommitIntent NewIntent(string intentId) =>
        new(
            intentId: intentId,
            workflowExecutionId: "wfexec-1",
            kind: "DispatchBookmarkRegistration",
            recordedAt: _now,
            activityExecutionId: "actexec-1",
            idempotencyKey: $"checkpoint-1:{intentId}",
            payload: Json("""{"bookmarkId":"bookmark-1"}"""),
            metadata: new Dictionary<string, string>(),
            dependsOnWaitRegistrationId: "wait-1",
            waitFailurePolicy: RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow);

    private RuntimeCheckpointStateChangeSet NewStateChanges(
        WorkflowExecutionState? workflowState = null,
        RuntimeStateChange<WorkflowExecutionState>? workflowStateChange = null,
        SchedulerState? schedulerState = null,
        RuntimeStateChange<SchedulerState>? schedulerStateChange = null,
        ActivityExecutionState? activityState = null,
        RuntimeStateChange<ActivityExecutionState>? activityStateChange = null,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>>? bookmarks = null,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? durableValues = null,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>>? incidents = null,
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>>? operational = null) =>
        new(
            workflowExecution: workflowStateChange ?? new RuntimeStateChange<WorkflowExecutionState>(
                StateId: (workflowState ?? _workflowState).WorkflowExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: workflowState ?? _workflowState,
                Metadata: new Dictionary<string, string>()),
            scheduler: schedulerStateChange ?? new RuntimeStateChange<SchedulerState>(
                StateId: (schedulerState ?? _schedulerState).WorkflowExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: schedulerState ?? _schedulerState,
                Metadata: new Dictionary<string, string>()),
            activityExecutions:
            [
                activityStateChange ?? new RuntimeStateChange<ActivityExecutionState>(
                    StateId: (activityState ?? _activityState).Execution.ActivityExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: activityState ?? _activityState,
                    Metadata: new Dictionary<string, string>())
            ],
            bookmarks: bookmarks ??
            [
                NewBookmarkChange("bookmark-1", "bookmark-1")
            ],
            durableValues:
            durableValues ??
            [
                NewDurableValueChange("durable-1", "durable-1")
            ],
            incidents: incidents ??
            [
                NewIncidentChange("incident-1", "incident-1")
            ],
            operational: operational ??
            [
                new RuntimeStateChange<ExecutionLivenessState>(
                    StateId: _operationalState.OperationalStateId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: _operationalState,
                    Metadata: new Dictionary<string, string>())
            ]);

    private RuntimeStateChange<BookmarkState> NewBookmarkChange(
        string stateId,
        string bookmarkId,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Upsert,
        string workflowExecutionId = "wfexec-1") =>
        new(
            StateId: stateId,
            Operation: operation,
            State: new BookmarkState(
                BookmarkId: bookmarkId,
                WorkflowExecutionId: workflowExecutionId,
                ActivityExecutionId: "actexec-1",
                ExecutableNodeId: "node-1",
                ResumeTargetId: "node-resume-1",
                StimulusType: "delivery-status",
                StimulusHash: "sha256:delivery-status:order-123",
                Payload: Json("""{"expected":"delivered"}"""),
                Metadata: new Dictionary<string, string>(),
                CreatedAt: _now,
                ExpiresAt: null),
            Metadata: new Dictionary<string, string>());

    private RuntimeStateChange<IncidentState> NewIncidentChange(
        string stateId,
        string incidentId,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Append,
        string workflowExecutionId = "wfexec-1",
        IncidentStatus status = IncidentStatus.Blocking) =>
        new(
            StateId: stateId,
            Operation: operation,
            State: new IncidentState(
                incidentId: incidentId,
                workflowExecutionId: workflowExecutionId,
                activityExecutionId: _incidentState.ActivityExecutionId,
                executableNodeId: _incidentState.ExecutableNodeId,
                severity: _incidentState.Severity,
                status: status,
                resolutionAction: _incidentState.ResolutionAction,
                failureType: _incidentState.FailureType,
                message: _incidentState.Message,
                createdAt: _incidentState.CreatedAt,
                resolvedAt: status is IncidentStatus.Resolved or IncidentStatus.Suppressed ? _incidentState.CreatedAt.AddMinutes(1) : null,
                metadata: _incidentState.Metadata),
            Metadata: new Dictionary<string, string>());

    private RuntimeStateChange<DurableValueState> NewDurableValueChange(
        string stateId,
        string durableValueId,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Upsert,
        string workflowExecutionId = "wfexec-1") =>
        new(
            StateId: stateId,
            Operation: operation,
            State: new DurableValueState(
                durableValueId: durableValueId,
                workflowExecutionId: workflowExecutionId,
                valueId: _durableValueState.ValueId,
                type: _durableValueState.Type,
                lifecycle: _durableValueState.Lifecycle,
                storage: _durableValueState.Storage,
                inlineValue: _durableValueState.InlineValue,
                externalReference: _durableValueState.ExternalReference,
                sourceActivityExecutionId: _durableValueState.SourceActivityExecutionId,
                capturedAt: _durableValueState.CapturedAt,
                metadata: _durableValueState.Metadata),
            Metadata: new Dictionary<string, string>());

    private RuntimeStateChange<ExecutionLivenessState> NewOperationalChange(
        string stateId,
        string operationalStateId,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Upsert,
        string workflowExecutionId = "wfexec-1",
        string ownerId = "worker-1",
        long fencingToken = 1) =>
        new(
            StateId: stateId,
            Operation: operation,
            State: new ExecutionLivenessState(
                operationalStateId: operationalStateId,
                workflowExecutionId: workflowExecutionId,
                executionLease: new RuntimeExecutionLease(
                    leaseId: _operationalState.ExecutionLease!.LeaseId,
                    workflowExecutionId: workflowExecutionId,
                    ownerId: ownerId,
                    acquiredAt: _operationalState.ExecutionLease.AcquiredAt,
                    expiresAt: _operationalState.ExecutionLease.ExpiresAt,
                    fencingToken: fencingToken),
                heartbeat: new RuntimeHeartbeat(
                    heartbeatId: _operationalState.Heartbeat!.HeartbeatId,
                    workflowExecutionId: workflowExecutionId,
                    ownerId: ownerId,
                    leaseId: _operationalState.Heartbeat.LeaseId,
                    recordedAt: _operationalState.Heartbeat.RecordedAt),
                drain: _operationalState.Drain,
                interruptedExecution: _operationalState.InterruptedExecution,
                pendingPostCommitIntentIds: _operationalState.PendingPostCommitIntentIds,
                metadata: _operationalState.Metadata),
            Metadata: new Dictionary<string, string>());

    private RuntimeCheckpointCommitter NewCommitter(
        RuntimeCheckpointPersistenceMode mode,
        RecordingCommitStore commitStore) =>
        new(
            new FixedPolicy(mode),
            commitStore);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FixedPolicy(RuntimeCheckpointPersistenceMode mode) : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeCheckpointPersistenceDecision(mode));
    }

    private sealed class RecordingCommitStore(
        List<string>? events = null,
        Exception? exception = null,
        IReadOnlyCollection<string>? pendingPostCommitWorkIds = null) : IRuntimeCheckpointCommitStore
    {
        public List<(RuntimeCheckpointCommit Commit, RuntimeCheckpointPersistenceDecision Decision)> Commits { get; } = [];

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            events?.Add($"commit:{commit.CommitId}");

            if (exception is not null)
                throw exception;

            Commits.Add((commit, decision));
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult(pendingPostCommitWorkIds ?? commit.PostCommitIntents.Select(intent => $"{commit.CommitId}:{intent.IntentId}").ToArray()));
        }
    }

    private sealed class ThrowingWorkflowExecutionStateStore : IWorkflowExecutionStateStore
    {
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("workflow state projection failed");

        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutionState?>(null);

        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowExecutionState>>([]);
    }

    private sealed class ThrowingActivityExecutionStateStore : IActivityExecutionStateStore
    {
        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("activity state projection failed");

        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivityExecutionState?>(null);

        public ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ActivityExecutionState>>([]);
    }

    private sealed class ThrowingSchedulerStateStore : ISchedulerStateStore
    {
        public ValueTask<SchedulerState> SaveAsync(SchedulerState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("scheduler state projection failed");

        public ValueTask<SchedulerState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<SchedulerState?>(null);

        public ValueTask<IReadOnlyCollection<SchedulerState>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<SchedulerState>>([]);
    }

    private sealed class ThrowingBookmarkStateStore : IBookmarkStateStore
    {
        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("bookmark state projection failed");

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("bookmark state projection failed");

        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<BookmarkState?>(null);

        public ValueTask<IReadOnlyCollection<BookmarkState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<BookmarkState>>([]);
    }

    private sealed class ThrowingDurableValueStateStore : IDurableValueStateStore
    {
        public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("durable value state projection failed");

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("durable value state projection failed");

        public ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DurableValueState?>(null);

        public ValueTask<IReadOnlyCollection<DurableValueState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<DurableValueState>>([]);
    }

    private sealed class ThrowingIncidentStateStore : IIncidentStateStore
    {
        public ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("incident state projection failed");

        public ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("incident state projection failed");

        public ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IncidentState?>(null);

        public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<IncidentState>>([]);

        public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<IncidentState>>([]);
    }

    private sealed class ThrowingExecutionLivenessStateStore : IExecutionLivenessStateStore
    {
        public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("operational state projection failed");

        public ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ExecutionLivenessState?>(null);

        public ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ExecutionLivenessState>>([]);

        public ValueTask<IReadOnlyCollection<ExecutionLivenessState>> ListAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<ExecutionLivenessState>>([]);
    }
}
