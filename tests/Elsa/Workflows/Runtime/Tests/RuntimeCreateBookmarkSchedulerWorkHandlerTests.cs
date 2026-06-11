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
    private readonly InMemoryBookmarkStateStore _bookmarkStateStore = new();
    private readonly InMemoryRuntimeCheckpointWriter _checkpointWriter;

    public RuntimeCreateBookmarkSchedulerWorkHandlerTests()
    {
        _checkpointWriter = new InMemoryRuntimeCheckpointWriter(
            activityExecutionStateStore: _activityStateStore,
            bookmarkStateStore: _bookmarkStateStore);
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

        var write = Assert.Single(_checkpointWriter.ListWrites());
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
        Assert.Empty(_checkpointWriter.ListWrites());
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
        Assert.Empty(_checkpointWriter.ListWrites());
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingActivityExecutionBeforeWriting()
    {
        await _executableStore.SaveAsync(NewExecutable());
        var handler = NewHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewCreateBookmarkWorkItem()).AsTask());

        Assert.Contains("references missing activity execution 'actexec-1'", exception.Message);
        Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        Assert.Empty(_checkpointWriter.ListWrites());
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
        Assert.Empty(_checkpointWriter.ListWrites());
    }

    [Fact]
    public void CanHandle_AcceptsOnlyCreateBookmarkWork()
    {
        var handler = NewHandler();

        Assert.True(handler.CanHandle(NewCreateBookmarkWorkItem()));
        Assert.False(handler.CanHandle(NewCreateBookmarkWorkItem(commandKind: WorkflowExecutionCommandKind.Checkpoint)));
    }

    private WorkflowCreateBookmarkSchedulerWorkHandler NewHandler() =>
        new(
            _executableStore,
            _activityStateStore,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                _checkpointWriter,
                new NoopRuntimePostCommitIntentDispatcher()),
            new FixedTimeProvider(_now));

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
            metadata: new Dictionary<string, string> { ["customer"] = "northwind" }));

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
            nodes:
            [
                NewNode("node-wait", document.RootElement)
            ],
            edges: [],
            startNodeIds: [],
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
