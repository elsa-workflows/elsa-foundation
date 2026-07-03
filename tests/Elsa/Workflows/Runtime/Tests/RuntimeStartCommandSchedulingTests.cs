using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStartCommandSchedulingTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_EnqueuesWorkflowStartedCheckpointWithStartNodeSchedulingIntent()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-start", "node-other"], ["node-start"]);
        await store.SaveAsync(executable);
        var handler = NewHandler(store, queue);

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var checkpointWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.Checkpoint, checkpointWork.CommandKind);
        Assert.Equal("wfexec-1", checkpointWork.WorkflowExecutionId);
        Assert.Equal("start-work:checkpoint:WorkflowStarted", checkpointWork.WorkItemId);
        Assert.Equal(_now, checkpointWork.EnqueuedAt);
        Assert.Equal(_now, checkpointWork.RecordedAt);
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, checkpointPayload.CheckpointName);
        Assert.Equal(RuntimeCheckpointCommandPayload.WorkflowStartReason, checkpointPayload.Reason);
        Assert.Empty(checkpointPayload.ActivityExecutionIds);
        var intent = Assert.Single(checkpointPayload.PostCommitIntents);
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, intent.Kind);
        Assert.Null(intent.ActivityExecutionId);

        var scheduled = intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, scheduled.CommandKind);
        Assert.Equal("start-work:schedule:node-start", scheduled.WorkItemId);
        Assert.Equal(12, scheduled.Sequence);
        Assert.Equal(_now.ToString("O"), scheduled.CommandMetadata[RuntimeMetadataKeys.WorkflowStartedAt]);
        var payload = scheduled.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal("node-start", payload.ExecutableNodeId);
        Assert.Equal("actexec-1", payload.ActivityExecutionId);
        Assert.Equal(RuntimeScheduleActivityCommandPayload.WorkflowStartReason, payload.Reason);
        Assert.Equal(executable.Identity, payload.PinnedExecutable);
    }

    [Fact]
    public async Task HandleAsync_AttachesOneSchedulingIntentForRootActivity()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-a", "node-b", "node-c"], ["node-a"]);
        await store.SaveAsync(executable);
        var handler = NewHandler(store, queue);

        await handler.HandleAsync(NewStartWorkItem(executable.Identity));

        var checkpointWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        var intent = Assert.Single(checkpointPayload.PostCommitIntents);
        var scheduled = intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        var payload = scheduled.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal("node-a", payload.ExecutableNodeId);
        Assert.Equal("actexec-1", payload.ActivityExecutionId);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, scheduled.CommandKind);
    }

    [Fact]
    public async Task HandleAsync_RejectsPinnedExecutableMismatchBeforeScheduling()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-start"], ["node-start"]);
        await store.SaveAsync(executable);
        var pinned = executable.Identity with { ArtifactHash = "sha256:pinned" };
        var handler = NewHandler(store, queue);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(pinned)).AsTask());

        Assert.Contains("pinned executable artifact", exception.Message);
        Assert.Contains("definition-1/version-1", exception.Message);
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_IgnoresSourceReferenceWhenCheckingPinnedExecutableSnapshot()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-start"], ["node-start"]);
        await store.SaveAsync(executable);
        var pinned = executable.Identity with
        {
            Source = new WorkflowExecutableSourceReference("WorkflowDefinitionVersion", "version-1", "1.0.0")
        };
        var handler = NewHandler(store, queue);

        await handler.HandleAsync(NewStartWorkItem(pinned));

        var checkpointWork = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.Checkpoint, checkpointWork.CommandKind);
    }

    [Fact]
    public async Task DrainAsync_CommitsWorkflowStartedBeforeDispatchingStartNodeScheduling()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        var executable = NewExecutable(["node-start"], ["node-start"]);
        await store.SaveAsync(executable);
        var startHandler = NewHandler(store, queue);
        var checkpointHandler = NewCheckpointHandler(new InMemoryActivityExecutionStateStore(), checkpointWriter, queue);
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [startHandler, checkpointHandler, new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewStartWorkItem(executable.Identity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 2));

        Assert.Equal(2, result.DrainedCount);
        Assert.Collection(
            result.Items,
            first => Assert.Equal("start-work", first.WorkItemId),
            second => Assert.Equal("start-work:checkpoint:WorkflowStarted", second.WorkItemId));
        var write = Assert.Single(checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, write.Commit.Checkpoint.Name);
        var workflowChange = write.Commit.StateChanges.WorkflowExecution;
        Assert.NotNull(workflowChange);
        Assert.Equal("wfexec-1", workflowChange.StateId);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, workflowChange.Operation);
        Assert.Equal(WorkflowExecutionStatus.Running, workflowChange.State.Status);
        Assert.Equal(_now, workflowChange.State.CreatedAt);
        Assert.Equal(_now, workflowChange.State.StartedAt);
        Assert.Equal(_now, workflowChange.State.UpdatedAt);
        Assert.Null(workflowChange.State.CompletedAt);
        Assert.Equal(executable.Identity, workflowChange.State.PinnedExecutable);
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var pending = Assert.Single(await checkpointWriter.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 10, workflowExecutionId: "wfexec-1")));
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, pending.Intent.Kind);
        var scheduled = pending.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, scheduled.CommandKind);
        var schedulePayload = scheduled.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal("node-start", schedulePayload.ExecutableNodeId);
        Assert.Equal("actexec-1", schedulePayload.ActivityExecutionId);
    }

    [Fact]
    public async Task DrainAsync_SeedsSuppliedVariablesAndInputsAsDurableRuntimeState()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var durableValueStore = new InMemoryDurableValueStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: null,
            activityExecutionStateStore: new InMemoryActivityExecutionStateStore(),
            bookmarkStateStore: null,
            durableValueStateStore: durableValueStore,
            incidentStateStore: null,
            operationalStateStore: null,
            schedulerStateStore: null,
            activityExecutionInspectionWriter: null);
        var executable = NewExecutable(["node-start"], ["node-start"]);
        await store.SaveAsync(executable);
        var startHandler = NewHandler(store, queue);
        var checkpointHandler = NewCheckpointHandler(new InMemoryActivityExecutionStateStore(), checkpointWriter, queue);
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [startHandler, checkpointHandler, new NoopWorkflowSchedulerWorkHandler()],
            new FixedTimeProvider(_now));
        await queue.EnqueueAsync(NewStartWorkItem(executable.Identity, payload: NewStartPayloadWithSeed(executable.Identity)));

        await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 2));

        var write = Assert.Single(checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.WorkflowStarted, write.Commit.Checkpoint.Name);
        Assert.Equal(2, write.Commit.StateChanges.DurableValues.Count);

        var durableValues = await durableValueStore.ListAsync("wfexec-1");
        var variables = RuntimeInputBindingStateProjection.ProjectWorkflowVariables(durableValues);
        var inputs = RuntimeInputBindingStateProjection.ProjectWorkflowInputs(durableValues);
        Assert.Equal("Hello", ((JsonElement)Assert.Single(variables).Value!).GetString());
        Assert.Equal("World", ((JsonElement)Assert.Single(inputs).Value!).GetString());
    }

    private JsonElement NewStartPayloadWithSeed(WorkflowExecutableIdentity identity) =>
        JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(
            identity,
            "artifact-1",
            variables: WorkflowExecutionStartCommandPayload.ToJsonValues(new Dictionary<string, object?> { ["greeting"] = "Hello" }),
            inputs: WorkflowExecutionStartCommandPayload.ToJsonValues(new Dictionary<string, object?> { ["name"] = "World" })));

    [Fact]
    public async Task CheckpointHandler_DoesNotDispatchStartNodeSchedulingWhenWorkflowStartedWriteFails()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var startQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var dispatchQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-start"], ["node-start"]);
        await store.SaveAsync(executable);
        var startHandler = NewHandler(store, startQueue);
        await startHandler.HandleAsync(NewStartWorkItem(executable.Identity));
        var checkpointWork = Assert.Single(await startQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var checkpointHandler = NewCheckpointHandler(
            new InMemoryActivityExecutionStateStore(),
            new ThrowingCheckpointWriter(),
            dispatchQueue);

        await Assert.ThrowsAsync<InvalidOperationException>(() => checkpointHandler.HandleAsync(checkpointWork).AsTask());

        Assert.Empty(await dispatchQueue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_RejectsMissingStartPayloadBeforeScheduling()
    {
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = NewHandler(store, queue);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(includePayload: false)).AsTask());

        Assert.Contains("requires a start command payload", exception.Message);
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task HandleAsync_RejectsMalformedStartPayloadBeforeScheduling()
    {
        using var document = JsonDocument.Parse("[]");
        var store = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = NewHandler(store, queue);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(NewStartWorkItem(payload: document.RootElement.Clone())).AsTask());

        Assert.Contains("not a valid start command payload", exception.Message);
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public void CanHandle_AcceptsOnlyStartCommandWork()
    {
        var handler = new WorkflowStartSchedulerWorkHandler(
            new InMemoryWorkflowExecutableStore(),
            new InMemoryWorkflowSchedulerWorkQueue(),
            new IncrementingRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(_now));

        Assert.True(handler.CanHandle(NewStartWorkItem(NewIdentity())));
        Assert.False(handler.CanHandle(NewStartWorkItem(NewIdentity(), WorkflowExecutionCommandKind.ScheduleActivity)));
    }

    private RuntimeSchedulerWorkItem NewStartWorkItem(
        WorkflowExecutableIdentity? pinnedExecutable = null,
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.Start,
        JsonElement? payload = null,
        bool includePayload = true)
    {
        var resolvedPayload = includePayload
            ? payload ?? JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(
                pinnedExecutable ?? NewIdentity(),
                "artifact-1"))
            : (JsonElement?)null;

        return new RuntimeSchedulerWorkItem(
            workItemId: "start-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: commandKind,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:start:artifact-1",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 10,
            payload: resolvedPayload,
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private WorkflowStartSchedulerWorkHandler NewHandler(
        IWorkflowExecutableStore store,
        IWorkflowSchedulerWorkQueue queue) =>
        new(store, queue, new IncrementingRuntimeExecutionIdGenerator(), new FixedTimeProvider(_now));

    private WorkflowCheckpointSchedulerWorkHandler NewCheckpointHandler(
        IActivityExecutionStateStore activityStateStore,
        IRuntimeCheckpointCommitStore checkpointWriter,
        IWorkflowSchedulerWorkQueue queue) =>
        new(
            activityStateStore,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                checkpointWriter),
            inspectionAccumulator: null,
            timeProvider: new FixedTimeProvider(_now));

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string> nodeIds, IReadOnlyCollection<string> startNodeIds) =>
        new(
            identity: NewIdentity(),
            rootActivity: ToRootActivity(nodeIds.Select(NewNode).ToArray()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode ToRootActivity(IReadOnlyCollection<ExecutableNode> nodes)
    {
        var nodeSnapshot = nodes.ToArray();
        if (nodeSnapshot.Length == 1)
            return nodeSnapshot[0];

        var root = nodeSnapshot[0];
        return new ExecutableNode(
            executableNodeId: root.ExecutableNodeId,
            authoredActivityId: root.AuthoredActivityId,
            activityType: root.ActivityType,
            activityTypeVersion: root.ActivityTypeVersion,
            descriptorType: root.DescriptorType,
            descriptorPayload: root.DescriptorPayload,
            inputBindings: root.InputBindings,
            outputCaptures: root.OutputCaptures,
            metadata: root.Metadata,
            childSlots:
            [
                new ExecutableChildSlot("children", nodeSnapshot.Skip(1).ToArray())
            ]);
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static ExecutableNode NewNode(string nodeId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class IncrementingRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        private int _activityExecutionIndex;

        public string NewWorkflowExecutionId() => "wfexec-unused";

        public string NewWorkflowExecutionCommandId() => "command-unused";

        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-unused";

        public string NewActivityExecutionId() => $"actexec-{++_activityExecutionIndex}";
    }

    private sealed class ThrowingCheckpointWriter : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("checkpoint commit failed");
    }
}
