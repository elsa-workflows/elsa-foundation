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
            WorkflowExecutionId: "wfexec-1",
            Version: 1,
            PendingWork: [],
            VolatileWaits: []);
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
        Assert.Equal(RuntimeStateCategory.Operational, Assert.Single(commit.StateChanges.Operational).Category);
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
            IntentId: intentId,
            WorkflowExecutionId: "wfexec-1",
            Kind: "DispatchBookmarkRegistration",
            RecordedAt: _now,
            ActivityExecutionId: "actexec-1",
            IdempotencyKey: $"checkpoint-1:{intentId}",
            Payload: Json("""{"bookmarkId":"bookmark-1"}"""),
            Metadata: new Dictionary<string, string>());

    private RuntimeCheckpointStateChangeSet NewStateChanges(
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>>? bookmarks = null,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>>? incidents = null) =>
        new(
            workflowExecution: new RuntimeStateChange<WorkflowExecutionState>(
                StateId: _workflowState.WorkflowExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: _workflowState,
                Metadata: new Dictionary<string, string>()),
            scheduler: new RuntimeStateChange<SchedulerState>(
                StateId: _schedulerState.WorkflowExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: _schedulerState,
                Metadata: new Dictionary<string, string>()),
            activityExecutions:
            [
                new RuntimeStateChange<ActivityExecutionState>(
                    StateId: _activityState.Execution.ActivityExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: _activityState,
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
            operational:
            [
                new RuntimeStateChangeReference(
                    StateId: "lease-1",
                    Category: RuntimeStateCategory.Operational,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    WorkflowExecutionId: "wfexec-1",
                    ActivityExecutionId: null,
                    ResumeTargetId: null,
                    Metadata: new Dictionary<string, string>())
            ]);

    private RuntimeStateChange<BookmarkState> NewBookmarkChange(string stateId, string bookmarkId) =>
        new(
            StateId: stateId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: new BookmarkState(
                BookmarkId: bookmarkId,
                WorkflowExecutionId: "wfexec-1",
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
}
