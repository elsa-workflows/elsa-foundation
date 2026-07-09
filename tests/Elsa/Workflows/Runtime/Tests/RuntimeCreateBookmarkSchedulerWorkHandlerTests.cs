using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeCreateBookmarkSchedulerWorkHandlerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 18, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryActivityExecutionStateStore _activityStateStore = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
    private readonly InMemoryBookmarkStateStore _bookmarkStateStore = new();
    private readonly InMemoryRuntimeCheckpointCommitStore _checkpointWriter;

    public RuntimeCreateBookmarkSchedulerWorkHandlerTests()
    {
        _checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(null, _activityStateStore, _bookmarkStateStore, null, null, null, null, _inspectionStore);
    }

    [Fact]
    public async Task HandleAsync_CommitsBookmarkCreatedAndSuspendsActivity()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        var handler = NewHandler();

        await handler.HandleAsync(NewCreateBookmarkWorkItem());

        var bookmark = await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1");
        Assert.NotNull(bookmark);
        Assert.Equal("actexec-1", bookmark.ActivityExecutionId);
        Assert.Equal("node-wait", bookmark.ExecutableNodeId);
        Assert.Equal("resume-target:delivery", bookmark.ResumeTargetId);
        Assert.Equal("delivery-status", bookmark.StimulusType);
        Assert.Equal("sha256:delivery-status:order-123", bookmark.StimulusHash);
        Assert.Equal("order-123", bookmark.Payload!.Value.GetProperty("orderId").GetString());
        Assert.Equal(_now.AddMinutes(30), bookmark.ExpiresAt);
        Assert.Equal("northwind", bookmark.Metadata["customer"]);
        Assert.Equal(RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason, bookmark.Metadata["runtime.reason"]);

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Suspended, state.Status);
        Assert.Equal("BookmarkWaiting", state.SubStatus);
        Assert.Equal(["bookmark-1"], state.BookmarkIds);
        Assert.Equal("bookmark-1", state.Metadata["runtime.bookmarkId"]);
        Assert.Equal("resume-target:delivery", state.Metadata["runtime.resumeTargetId"]);

        var write = Assert.Single(_checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, write.Decision.Mode);
        Assert.Equal("commit:create-bookmark-work:bookmark-created:bookmark-1", write.Commit.CommitId);
        Assert.Equal("checkpoint:create-bookmark-work:bookmark-created:bookmark-1", write.Commit.Checkpoint.CheckpointId);
        Assert.Equal(RuntimeCheckpointNames.BookmarkCreated, write.Commit.Checkpoint.Name);
        Assert.Equal(["actexec-1"], write.Commit.Checkpoint.ActivityExecutionIds);
        Assert.Empty(write.Commit.PostCommitIntents);

        var activityChange = Assert.Single(write.Commit.StateChanges.ActivityExecutions);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, activityChange.Operation);
        Assert.Equal(ActivityExecutionStatus.Suspended, activityChange.State.Status);

        var bookmarkChange = Assert.Single(write.Commit.StateChanges.Bookmarks);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, bookmarkChange.Operation);
        Assert.Equal("bookmark-1", bookmarkChange.State.BookmarkId);

        var inspectionChange = Assert.Single(write.Commit.StateChanges.ActivityExecutionInspections);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, inspectionChange.Operation);
        Assert.Equal(ActivityExecutionStatus.Suspended, inspectionChange.State.Status);
        var bookmarkSummary = Assert.Single(inspectionChange.State.Bookmarks);
        Assert.Equal("bookmark-1", bookmarkSummary.BookmarkId);
        Assert.Equal("resume-target:delivery", bookmarkSummary.ResumeTargetId);
        Assert.Equal("delivery-status", bookmarkSummary.StimulusType);
        Assert.Equal("sha256:delivery-status:order-123", bookmarkSummary.StimulusHash);
        var snapshot = Assert.Single(inspectionChange.State.ValueSnapshots);
        Assert.Equal("Text", snapshot.Name);
        Assert.Equal(ActivityExecutionInspectionValueSubject.ActivityInput, snapshot.Subject);
        Assert.Equal(RuntimePayloadCaptureMode.Payload, snapshot.CaptureMode);

        var projection = await _inspectionStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(projection);
        Assert.Equal(ActivityExecutionStatus.Suspended, projection.Status);
        Assert.Single(projection.Bookmarks);
    }

    [Fact]
    public async Task HandleAsync_KeepsBookmarkIdsDuplicateFreeOnReplay()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState() with
        {
            Status = ActivityExecutionStatus.Suspended,
            SubStatus = "BookmarkWaiting",
            BookmarkIds = ["bookmark-1"]
        });
        var handler = NewHandler();

        await handler.HandleAsync(NewCreateBookmarkWorkItem());

        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Suspended, state.Status);
        Assert.Equal(["bookmark-1"], state.BookmarkIds);
    }

    [Fact]
    public async Task HandleAsync_DoesNotRewriteTerminalActivityState()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState() with
        {
            Status = ActivityExecutionStatus.Completed,
            CompletedAt = _now.AddMinutes(-1)
        });
        var handler = NewHandler();

        await handler.HandleAsync(NewCreateBookmarkWorkItem());

        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Completed, state.Status);
        Assert.Empty(state.BookmarkIds);
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task HandleAsync_DoesNotRewriteRecoveredActivityState()
    {
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState() with
        {
            Status = ActivityExecutionStatus.Recovered,
            CompletedAt = _now.AddMinutes(-1)
        });
        var handler = NewHandler();

        await handler.HandleAsync(NewCreateBookmarkWorkItem());

        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        var state = await _activityStateStore.FindAsync("wfexec-1", "actexec-1");
        Assert.NotNull(state);
        Assert.Equal(ActivityExecutionStatus.Recovered, state.Status);
        Assert.Empty(state.BookmarkIds);
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingResumeTargetBeforeWriting()
    {
        await _executableStore.SaveAsync(NewExecutable(includeResumeTarget: false));
        await _activityStateStore.SaveAsync(NewRunningState());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewCreateBookmarkWorkItem()).AsTask());

        Assert.Contains("references resume target 'resume-target:delivery'", exception.Message);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Equal(ActivityExecutionStatus.Running, (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.Status);
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingActivityExecutionBeforeWriting()
    {
        await _executableStore.SaveAsync(NewExecutable());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewCreateBookmarkWorkItem()).AsTask());

        Assert.Contains("references missing activity execution 'actexec-1'", exception.Message);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task HandleAsync_RejectsPinnedArtifactMismatchBeforeWriting()
    {
        await _executableStore.SaveAsync(NewExecutable(identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "2.0.0", "sha256:changed")));
        await _activityStateStore.SaveAsync(NewRunningState());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewCreateBookmarkWorkItem()).AsTask());

        Assert.Contains("but pinned executable artifact", exception.Message);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public void CanHandle_AcceptsOnlyCreateBookmarkWork()
    {
        var handler = NewHandler();

        Assert.True(handler.CanHandle(NewCreateBookmarkWorkItem()));
        Assert.False(handler.CanHandle(NewCreateBookmarkWorkItem(commandKind: WorkflowExecutionCommandKind.Checkpoint)));
    }

    [Fact]
    public async Task HandleAsync_NotifiesLifecycleObservers_AfterTheDurableCommit()
    {
        // Spec 089 D (T005): a bookmark-lifecycle observer is invoked with the committed bookmark AFTER the commit.
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        var observer = new RecordingBookmarkLifecycleObserver();
        var handler = NewHandler(new BookmarkLifecycleNotifier([observer]));

        await handler.HandleAsync(NewCreateBookmarkWorkItem());

        var created = Assert.Single(observer.Created);
        Assert.Equal("bookmark-1", created.BookmarkId);
        Assert.Equal("delivery-status", created.StimulusType);
        Assert.Empty(observer.Consumed);
        // The bookmark was durably committed before the observer saw it.
        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
    }

    [Fact]
    public async Task HandleAsync_ThrowingObserver_DoesNotFaultTheRun()
    {
        // The observer fires on the run path: a throw is caught and logged, the run still succeeds.
        await _executableStore.SaveAsync(NewExecutable());
        await _activityStateStore.SaveAsync(NewRunningState());
        var handler = NewHandler(new BookmarkLifecycleNotifier([new ThrowingBookmarkLifecycleObserver()]));

        await handler.HandleAsync(NewCreateBookmarkWorkItem());

        Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Equal(ActivityExecutionStatus.Suspended, (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.Status);
    }

    private WorkflowCreateBookmarkSchedulerWorkHandler NewHandler(BookmarkLifecycleNotifier? notifier = null) =>
        new(
            _executableStore,
            _activityStateStore,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                _checkpointWriter),
            new RuntimeActivityExecutionInspectionAccumulator(_inspectionStore),
            new FixedTimeProvider(_now),
            notifier);

    private RuntimeSchedulerWorkItem NewCreateBookmarkWorkItem(
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.CreateBookmark,
        string executableNodeId = "node-wait")
    {
        var payload = JsonSerializer.SerializeToElement(new RuntimeCreateBookmarkCommandPayload(
            pinnedExecutable: NewIdentity(),
            bookmarkId: "bookmark-1",
            activityExecutionId: "actexec-1",
            executableNodeId: executableNodeId,
            resumeTargetId: "resume-target:delivery",
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            payload: Json("""{"orderId":"order-123"}"""),
            expiresAt: _now.AddMinutes(30),
            reason: RuntimeCreateBookmarkCommandPayload.ActivitySuspendedReason,
            metadata: new Dictionary<string, string> { ["customer"] = "northwind" },
            valueSnapshots: [NewInputSnapshot()]));

        return new RuntimeSchedulerWorkItem(
            workItemId: "create-bookmark-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:create-bookmark:bookmark-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 10,
            payload: payload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-1",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-wait",
                AuthoredActivityId: "authored-node-wait",
                ActivityType: "test/activity",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private ActivityExecutionInspectionValueSnapshot NewInputSnapshot() =>
        new(
            Name: "Text",
            Subject: ActivityExecutionInspectionValueSubject.ActivityInput,
            CaptureMode: RuntimePayloadCaptureMode.Payload,
            Type: new RuntimeValueTypeDescriptor("primitive", "string", null),
            CapturedAt: _now,
            Payload: JsonSerializer.SerializeToElement("hello"),
            CaptureReason: "Test capture",
            IsSensitive: false,
            Metadata: new Dictionary<string, string>());

    private static WorkflowExecutable NewExecutable(
        bool includeResumeTarget = true,
        WorkflowExecutableIdentity? identity = null)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var resumeTargets = includeResumeTarget
            ? new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-target:delivery"] = new(
                    ResumeTargetId: "resume-target:delivery",
                    ExecutableNodeId: "node-wait",
                    HandlerKey: "test-handler",
                    Metadata: new Dictionary<string, string>())
            }
            : new Dictionary<string, WorkflowExecutableResumeTarget>();

        return new(
            identity: identity ?? NewIdentity(),
            rootActivity: NewNode("node-wait", document.RootElement),
            resumeTargets: resumeTargets,
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode NewNode(string nodeId, JsonElement descriptorPayload) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: descriptorPayload.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
