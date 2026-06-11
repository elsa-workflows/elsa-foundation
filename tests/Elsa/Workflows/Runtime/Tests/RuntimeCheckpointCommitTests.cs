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
    private readonly OperationalState _operationalState;

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
            ScheduledAt: _now,
            StartedAt: _now,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: "branch-1",
            IterationId: null,
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
        _operationalState = new OperationalState(
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
        var immediateWriter = new RecordingWriter();
        var deferredWriter = new RecordingWriter();

        await NewCommitter(RuntimeCheckpointPersistenceMode.Immediate, immediateWriter).CommitAsync(commit);
        await NewCommitter(RuntimeCheckpointPersistenceMode.Deferred, deferredWriter).CommitAsync(commit);

        var immediateWrite = Assert.Single(immediateWriter.Writes);
        var deferredWrite = Assert.Single(deferredWriter.Writes);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, immediateWrite.Decision.Mode);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Deferred, deferredWrite.Decision.Mode);
        Assert.Equal(immediateWrite.Commit.Checkpoint, deferredWrite.Commit.Checkpoint);
        Assert.Equal(immediateWrite.Commit.StateChanges.WorkflowExecution!.State, deferredWrite.Commit.StateChanges.WorkflowExecution!.State);
        Assert.Equal(immediateWrite.Commit.StateChanges.Scheduler!.State, deferredWrite.Commit.StateChanges.Scheduler!.State);
        Assert.Equal(
            immediateWrite.Commit.StateChanges.ActivityExecutions.Select(change => change.StateId),
            deferredWrite.Commit.StateChanges.ActivityExecutions.Select(change => change.StateId));
        Assert.Equal(
            immediateWrite.Commit.StateChanges.DurableValues.Select(change => change.StateId),
            deferredWrite.Commit.StateChanges.DurableValues.Select(change => change.StateId));
        Assert.Equal(
            immediateWrite.Commit.StateChanges.Bookmarks.Select(change => change.State.ResumeTargetId),
            deferredWrite.Commit.StateChanges.Bookmarks.Select(change => change.State.ResumeTargetId));
    }

    [Fact]
    public async Task CheckpointCommitter_DispatchesPostCommitIntentsAfterSuccessfulWrite()
    {
        var events = new List<string>();
        var writer = new RecordingWriter(events);
        var dispatcher = new RecordingDispatcher(events);

        await NewCommitter(RuntimeCheckpointPersistenceMode.Immediate, writer, dispatcher).CommitAsync(NewCommit(RuntimeCheckpointNames.BookmarkCreated));

        Assert.Equal(["write:commit-1", "dispatch:intent-1"], events);
        Assert.Single(writer.Writes);
        Assert.Single(dispatcher.Intents);
    }

    [Fact]
    public async Task CheckpointCommitter_DoesNotDispatchPostCommitIntentsWhenWriteFails()
    {
        var events = new List<string>();
        var writer = new RecordingWriter(events, new InvalidOperationException("checkpoint write failed"));
        var dispatcher = new RecordingDispatcher(events);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await NewCommitter(RuntimeCheckpointPersistenceMode.Immediate, writer, dispatcher).CommitAsync(NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded)));

        Assert.Equal(["write:commit-1"], events);
        Assert.Empty(dispatcher.Intents);
    }

    [Fact]
    public async Task CheckpointCommitter_ReportsPartialPostCommitIntentDispatchFailures()
    {
        var events = new List<string>();
        var writer = new RecordingWriter(events);
        var dispatcher = new RecordingDispatcher(events, failOnIntentId: "intent-2", failure: new InvalidOperationException("Intent failed."));
        var commit = NewCommit(
            RuntimeCheckpointNames.PostCommitIntentRecorded,
            [
                NewIntent("intent-1"),
                NewIntent("intent-2"),
                NewIntent("intent-3")
            ]);

        var exception = await Assert.ThrowsAsync<RuntimePostCommitIntentDispatchException>(async () =>
            await NewCommitter(RuntimeCheckpointPersistenceMode.Immediate, writer, dispatcher).CommitAsync(commit));

        Assert.Equal("commit-1", exception.CommitId);
        Assert.Equal("intent-2", exception.FailedIntentId);
        Assert.Equal(["intent-1"], exception.DispatchedIntentIds);
        Assert.Equal(["intent-3"], exception.UndispatchedIntentIds);
        Assert.Equal(["write:commit-1", "dispatch:intent-1", "dispatch:intent-2"], events);
    }

    [Fact]
    public async Task CheckpointCommitter_DoesNotWrapPostCommitIntentDispatchCancellation()
    {
        var dispatcher = new RecordingDispatcher(
            failOnIntentId: "intent-1",
            failure: new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await NewCommitter(RuntimeCheckpointPersistenceMode.Immediate, new RecordingWriter(), dispatcher).CommitAsync(NewCommit(RuntimeCheckpointNames.PostCommitIntentRecorded)));
    }

    [Fact]
    public async Task CheckpointCommitter_DoesNotWriteOrDispatchWhenPolicySkipsPersistence()
    {
        var writer = new RecordingWriter();
        var dispatcher = new RecordingDispatcher();

        var decision = await NewCommitter(RuntimeCheckpointPersistenceMode.Skip, writer, dispatcher).CommitAsync(NewCommit(RuntimeCheckpointNames.WorkflowCompleted));

        Assert.Equal(RuntimeCheckpointPersistenceMode.Skip, decision.Mode);
        Assert.Empty(writer.Writes);
        Assert.Empty(dispatcher.Intents);
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_IsIdempotentByCommitId()
    {
        var writer = new InMemoryRuntimeCheckpointWriter();
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.ActivityStarted);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.ActivityCompleted);

        await writer.WriteAsync(first, decision);
        await writer.WriteAsync(first, decision);
        await writer.WriteAsync(conflictingReplay, decision);

        var write = Assert.Single(writer.ListWrites());
        Assert.Equal("commit-1", write.Commit.CommitId);
        Assert.Equal(RuntimeCheckpointNames.ActivityStarted, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_ProjectsWorkflowExecutionStateChanges()
    {
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(workflowStateStore);
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

        await writer.WriteAsync(running, decision);
        await writer.WriteAsync(completed, decision);

        var state = await workflowStateStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(5), state.CompletedAt);
        Assert.Equal(2, writer.ListWrites().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_DoesNotProjectConflictingReplay()
    {
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(workflowStateStore);
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

        await writer.WriteAsync(first, decision);
        await writer.WriteAsync(conflictingReplay, decision);

        var state = await workflowStateStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Running, state.Status);
        Assert.Null(state.CompletedAt);
        var write = Assert.Single(writer.ListWrites());
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_RejectsUnsupportedWorkflowStateProjectionBeforeRecordingWrite()
    {
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(workflowStateStore);
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Empty(writer.ListWrites());
        Assert.Empty(await workflowStateStore.ListAsync());
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_DoesNotRecordWhenWorkflowStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointWriter(new ThrowingWorkflowExecutionStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.WorkflowStarted);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListWrites());
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_DoesNotRecordWhenActivityStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointWriter(activityExecutionStateStore: new ThrowingActivityExecutionStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.ActivityCompleted);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListWrites());
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_DoesNotRecordWhenBookmarkStateProjectionFails()
    {
        var writer = new InMemoryRuntimeCheckpointWriter(bookmarkStateStore: new ThrowingBookmarkStateStore());
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.BookmarkCreated);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(commit, decision).AsTask());

        Assert.Empty(writer.ListWrites());
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_ProjectsActivityExecutionStateChanges()
    {
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(activityExecutionStateStore: activityStateStore);
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

        await writer.WriteAsync(running, decision);
        await writer.WriteAsync(completed, decision);

        var state = await activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(5), state.CompletedAt);
        Assert.Equal(2, writer.ListWrites().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_RejectsUnsupportedActivityStateProjectionBeforeRecordingWrite()
    {
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(activityExecutionStateStore: activityStateStore);
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Empty(writer.ListWrites());
        Assert.Empty(await activityStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_DoesNotProjectConflictingActivityReplay()
    {
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(activityExecutionStateStore: activityStateStore);
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

        await writer.WriteAsync(first, decision);
        await writer.WriteAsync(conflictingReplay, decision);

        var state = await activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.Null(state.CompletedAt);
        var write = Assert.Single(writer.ListWrites());
        Assert.Equal(RuntimeCheckpointNames.ActivityStarted, write.Commit.Checkpoint.Name);
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_ProjectsBookmarkStateChanges()
    {
        var bookmarkStateStore = new InMemoryBookmarkStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(bookmarkStateStore: bookmarkStateStore);
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

        await writer.WriteAsync(created, decision);

        var bookmark = await bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);
        Assert.Equal("node-resume-1", bookmark.ResumeTargetId);

        await writer.WriteAsync(consumed, decision);

        Assert.Null(await bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Equal(2, writer.ListWrites().Count);
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_RejectsUnsupportedBookmarkStateProjectionBeforeRecordingWrite()
    {
        var bookmarkStateStore = new InMemoryBookmarkStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(bookmarkStateStore: bookmarkStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.BookmarkCreated) with
        {
            StateChanges = NewStateChanges(bookmarks:
            [
                NewBookmarkChange("bookmark-1", "bookmark-1", RuntimeStateChangeOperation.Append)
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(commit, decision).AsTask());

        Assert.Contains("Upsert", exception.Message);
        Assert.Contains("Delete", exception.Message);
        Assert.Empty(writer.ListWrites());
        Assert.Empty(await bookmarkStateStore.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_RejectsBookmarkStateFromDifferentWorkflowBeforeRecordingWrite()
    {
        var bookmarkStateStore = new InMemoryBookmarkStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(bookmarkStateStore: bookmarkStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var commit = NewCommit(RuntimeCheckpointNames.BookmarkCreated) with
        {
            StateChanges = NewStateChanges(bookmarks:
            [
                NewBookmarkChange("bookmark-1", "bookmark-1", workflowExecutionId: "wfexec-2")
            ])
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(commit, decision).AsTask());

        Assert.Contains("WorkflowExecutionId", exception.Message);
        Assert.Empty(writer.ListWrites());
        Assert.Empty(await bookmarkStateStore.ListAsync("wfexec-1"));
        Assert.Empty(await bookmarkStateStore.ListAsync("wfexec-2"));
    }

    [Fact]
    public async Task InMemoryCheckpointWriter_DoesNotProjectConflictingBookmarkReplay()
    {
        var bookmarkStateStore = new InMemoryBookmarkStateStore();
        var writer = new InMemoryRuntimeCheckpointWriter(bookmarkStateStore: bookmarkStateStore);
        var decision = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        var first = NewCommit(RuntimeCheckpointNames.BookmarkCreated);
        var conflictingReplay = NewCommit(RuntimeCheckpointNames.BookmarkConsumed) with
        {
            StateChanges = NewStateChanges(bookmarks:
            [
                NewBookmarkChange("bookmark-1", "bookmark-1", RuntimeStateChangeOperation.Delete)
            ])
        };

        await writer.WriteAsync(first, decision);
        await writer.WriteAsync(conflictingReplay, decision);

        var bookmark = await bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);
        Assert.Equal("node-resume-1", bookmark.ResumeTargetId);
        var write = Assert.Single(writer.ListWrites());
        Assert.Equal(RuntimeCheckpointNames.BookmarkCreated, write.Commit.Checkpoint.Name);
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
    public void RuntimeCheckpointStateChangeSet_RejectsMismatchedOperationalStateIds()
    {
        var invalidOperational = new[]
        {
            new RuntimeStateChange<OperationalState>(
                StateId: "lease-1",
                Operation: RuntimeStateChangeOperation.Upsert,
                State: _operationalState,
                Metadata: new Dictionary<string, string>())
        };

        var exception = Assert.Throws<ArgumentException>(() => NewStateChanges(operational: invalidOperational));

        Assert.Contains("OperationalState.OperationalStateId", exception.Message);
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
        ActivityExecutionState? activityState = null,
        RuntimeStateChange<ActivityExecutionState>? activityStateChange = null,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>>? bookmarks = null,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>>? incidents = null,
        IReadOnlyCollection<RuntimeStateChange<OperationalState>>? operational = null) =>
        new(
            workflowExecution: workflowStateChange ?? new RuntimeStateChange<WorkflowExecutionState>(
                StateId: (workflowState ?? _workflowState).WorkflowExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: workflowState ?? _workflowState,
                Metadata: new Dictionary<string, string>()),
            scheduler: new RuntimeStateChange<SchedulerState>(
                StateId: _schedulerState.WorkflowExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: _schedulerState,
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
            [
                new RuntimeStateChange<DurableValueState>(
                    StateId: _durableValueState.DurableValueId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: _durableValueState,
                    Metadata: new Dictionary<string, string>())
            ],
            incidents: incidents ??
            [
                new RuntimeStateChange<IncidentState>(
                    StateId: _incidentState.IncidentId,
                    Operation: RuntimeStateChangeOperation.Append,
                    State: _incidentState,
                    Metadata: new Dictionary<string, string>())
            ],
            operational: operational ??
            [
                new RuntimeStateChange<OperationalState>(
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

    private RuntimeCheckpointCommitter NewCommitter(
        RuntimeCheckpointPersistenceMode mode,
        RecordingWriter writer,
        RecordingDispatcher? dispatcher = null) =>
        new(
            new FixedPolicy(mode),
            writer,
            dispatcher ?? new RecordingDispatcher());

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

    private sealed class RecordingWriter(List<string>? events = null, Exception? exception = null) : IRuntimeCheckpointWriter
    {
        public List<(RuntimeCheckpointCommit Commit, RuntimeCheckpointPersistenceDecision Decision)> Writes { get; } = [];

        public ValueTask WriteAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            events?.Add($"write:{commit.CommitId}");

            if (exception is not null)
                throw exception;

            Writes.Add((commit, decision));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDispatcher(
        List<string>? events = null,
        string? failOnIntentId = null,
        Exception? failure = null) : IRuntimePostCommitIntentDispatcher
    {
        public List<RuntimePostCommitIntent> Intents { get; } = [];

        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            events?.Add($"dispatch:{intent.IntentId}");

            if (intent.IntentId == failOnIntentId)
                throw failure ?? new InvalidOperationException($"Intent {intent.IntentId} failed.");

            Intents.Add(intent);
            return ValueTask.CompletedTask;
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
}
