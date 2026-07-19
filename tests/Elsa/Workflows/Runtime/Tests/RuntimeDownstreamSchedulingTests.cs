using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDownstreamSchedulingTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _checkpointExecutableStore = new();

    public RuntimeDownstreamSchedulingTests() =>
        _checkpointExecutableStore.SaveAsync(NewExecutable(["node-source"]))
            .AsTask().GetAwaiter().GetResult();

    [Fact]
    public async Task CompleteActivityHandler_DoesNotTraverseWorkflowLevelEdges()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-source", "node-next", "node-other"]);
        await executableStore.SaveAsync(executable);
        var handler = NewCompleteHandler(queue, executableStore);

        await handler.HandleAsync(NewCompleteWorkItem(
            executable.Identity,
            outcomeNames: ["Done"],
            completionKind: SchedulerCompletionKind.ContinuationScheduling));

        var checkpointWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(WorkflowExecutionCommandKind.Checkpoint, checkpointWork.CommandKind);
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, checkpointPayload.CheckpointName);
        Assert.Empty(checkpointPayload.PostCommitIntents);
    }

    [Fact]
    public async Task RootActivityCompletion_DrainsThroughContinuationCheckpointAndWorkflowCompletion()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        var executable = NewExecutable(["node-source", "node-next"]);
        await executableStore.SaveAsync(executable);
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        await queue.EnqueueAsync(NewCompleteWorkItem(
            executable.Identity,
            outcomeNames: ["Done"],
            completionKind: SchedulerCompletionKind.ActivityCompleted));
        var completeHandler = new WorkflowCompleteActivitySchedulerWorkHandler(
            activityStateStore,
            queue,
            executableStore,
            new FixedTimeProvider(_now));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [completeHandler, NewCheckpointHandler(activityStateStore, checkpointWriter, queue)],
            new FixedTimeProvider(_now));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 3));

        Assert.Equal(3, result.DrainedCount);
        Assert.All(result.Items, item => Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status));
        Assert.Collection(
            result.Items,
            first => Assert.Equal("work-1", first.WorkItemId),
            second => Assert.Equal("work-1:continuation:actexec-source", second.WorkItemId),
            third => Assert.Equal("work-1:continuation:actexec-source:checkpoint:WorkflowCompleted:actexec-source", third.WorkItemId));
        var write = Assert.Single(checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, write.Commit.Checkpoint.Name);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task CompleteActivityHandler_EnqueuesWorkflowCompletedCheckpointWhenNoOutcomeEdgesMatch()
    {
        var executableStore = new InMemoryWorkflowExecutableStore();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var executable = NewExecutable(["node-source", "node-next"]);
        await executableStore.SaveAsync(executable);
        var handler = NewCompleteHandler(queue, executableStore);

        await handler.HandleAsync(NewCompleteWorkItem(
            executable.Identity,
            outcomeNames: ["Other"],
            completionKind: SchedulerCompletionKind.ContinuationScheduling));

        var checkpointWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, checkpointPayload.CheckpointName);
        Assert.Empty(checkpointPayload.PostCommitIntents);
    }

    [Fact]
    public async Task CompleteActivityHandler_KeepsEmptyOutcomeContinuationNonTerminal()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewCompleteWorkItem(
            NewIdentity(),
            outcomeNames: [],
            completionKind: SchedulerCompletionKind.ContinuationScheduling));

        var checkpointWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.ActivityCompleted, checkpointPayload.CheckpointName);
        Assert.Empty(checkpointPayload.PostCommitIntents);
    }

    [Fact]
    public async Task CompleteActivityHandler_TreatsOutcomeContinuationAsTerminalWithoutWorkflowTraversalServices()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            new InMemoryActivityExecutionStateStore(),
            queue,
            new FixedTimeProvider(_now));

        await handler.HandleAsync(NewCompleteWorkItem(
            NewIdentity(),
            outcomeNames: ["Done"],
            completionKind: SchedulerCompletionKind.ContinuationScheduling));

        var checkpointWork = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var checkpointPayload = checkpointWork.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, checkpointPayload.CheckpointName);
        Assert.Empty(checkpointPayload.PostCommitIntents);
    }

    [Fact]
    public async Task CheckpointHandler_CommitsWorkflowExecutionStateForWorkflowCompletedCheckpoint()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter, queue);

        await handler.HandleAsync(NewCheckpointWorkItem([], RuntimeCheckpointNames.WorkflowCompleted));

        var write = Assert.Single(checkpointWriter.ListCommits());
        Assert.Equal(RuntimeCheckpointNames.WorkflowCompleted, write.Commit.Checkpoint.Name);
        var workflowChange = write.Commit.StateChanges.WorkflowExecution;
        Assert.NotNull(workflowChange);
        Assert.Equal("wfexec-1", workflowChange.StateId);
        Assert.Equal(RuntimeStateChangeOperation.Upsert, workflowChange.Operation);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowChange.State.Status);
        Assert.Equal(_now, workflowChange.State.CreatedAt);
        Assert.Null(workflowChange.State.StartedAt);
        Assert.Equal(_now, workflowChange.State.UpdatedAt);
        Assert.Equal(_now, workflowChange.State.CompletedAt);
        Assert.Equal(NewIdentity(), workflowChange.State.PinnedExecutable);
    }

    [Fact]
    public async Task CheckpointHandler_PreservesWorkflowStartedAtMetadataForWorkflowCompletedCheckpoint()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        var startedAt = _now.AddMinutes(-5);
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter, queue);

        await handler.HandleAsync(NewCheckpointWorkItem(
            [],
            RuntimeCheckpointNames.WorkflowCompleted,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.WorkflowStartedAt] = startedAt.ToString("O", CultureInfo.InvariantCulture)
            }));

        var write = Assert.Single(checkpointWriter.ListCommits());
        var workflowChange = write.Commit.StateChanges.WorkflowExecution;
        Assert.NotNull(workflowChange);
        Assert.Equal(startedAt, workflowChange.State.CreatedAt);
        Assert.Equal(startedAt, workflowChange.State.StartedAt);
        Assert.Equal(_now, workflowChange.State.CompletedAt);
    }

    [Fact]
    public async Task CheckpointHandler_UsesWorkflowStartedAtMetadataForWorkflowStartedCheckpoint()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        var startedAt = _now.AddMinutes(-5);
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter, queue);

        await handler.HandleAsync(NewCheckpointWorkItem(
            [],
            RuntimeCheckpointNames.WorkflowStarted,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.WorkflowStartedAt] = startedAt.ToString("O", CultureInfo.InvariantCulture)
            },
            []));

        var write = Assert.Single(checkpointWriter.ListCommits());
        var workflowChange = write.Commit.StateChanges.WorkflowExecution;
        Assert.NotNull(workflowChange);
        Assert.Equal(WorkflowExecutionStatus.Running, workflowChange.State.Status);
        Assert.Equal(startedAt, workflowChange.State.CreatedAt);
        Assert.Equal(startedAt, workflowChange.State.StartedAt);
        Assert.Equal(_now, workflowChange.State.UpdatedAt);
        Assert.Null(workflowChange.State.CompletedAt);
    }

    [Fact]
    public async Task CheckpointHandler_ProjectsWorkflowStartedStateToWorkflowExecutionStore()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowStateStore,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        var startedAt = _now.AddMinutes(-5);
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter, queue);

        await handler.HandleAsync(NewCheckpointWorkItem(
            [],
            RuntimeCheckpointNames.WorkflowStarted,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.WorkflowStartedAt] = startedAt.ToString("O", CultureInfo.InvariantCulture)
            },
            []));

        var state = await workflowStateStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Running, state.Status);
        Assert.Equal(startedAt, state.CreatedAt);
        Assert.Equal(startedAt, state.StartedAt);
        Assert.Equal(_now, state.UpdatedAt);
        Assert.Null(state.CompletedAt);
    }

    [Fact]
    public async Task CheckpointHandler_ProjectsWorkflowCompletedStateToWorkflowExecutionStore()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowStateStore,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        var startedAt = _now.AddMinutes(-5);
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter, queue);

        await handler.HandleAsync(NewCheckpointWorkItem(
            [],
            RuntimeCheckpointNames.WorkflowCompleted,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.WorkflowStartedAt] = startedAt.ToString("O", CultureInfo.InvariantCulture)
            }));

        var state = await workflowStateStore.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(startedAt, state.StartedAt);
        Assert.Equal(_now, state.CompletedAt);
    }

    [Fact]
    public async Task CheckpointHandler_PreservesPinnedRunKindThroughCompletion()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        await workflowStateStore.SaveAsync(new WorkflowExecutionState(
            "wfexec-1",
            NewIdentity(),
            WorkflowExecutionStatus.Running,
            null,
            _now.AddMinutes(-5),
            _now.AddMinutes(-5),
            _now.AddMinutes(-5),
            null,
            null,
            null,
            null,
            new Dictionary<string, string>())
        {
            RunKind = WorkflowRunKind.TestRun
        });
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore(
            workflowStateStore,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter, queue, workflowStateStore);

        await handler.HandleAsync(NewCheckpointWorkItem([], RuntimeCheckpointNames.WorkflowCompleted));

        var state = await workflowStateStore.FindAsync("wfexec-1");
        Assert.Equal(WorkflowRunKind.TestRun, state!.RunKind);
    }

    [Fact]
    public async Task CheckpointHandler_RecordsSchedulerIntentAfterSuccessfulCommit()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        var checkpointWriter = new InMemoryRuntimeCheckpointCommitStore();
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        var handler = NewCheckpointHandler(activityStateStore, checkpointWriter, queue);
        var downstreamWork = NewScheduleWorkItem();

        await handler.HandleAsync(NewCheckpointWorkItem([NewSchedulerIntent(downstreamWork)]));

        Assert.Single(checkpointWriter.ListCommits());
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var pending = Assert.Single(await checkpointWriter.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 10, workflowExecutionId: "wfexec-1")));
        var queued = pending.Intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal(downstreamWork.WorkItemId, queued.WorkItemId);
        Assert.Equal(WorkflowExecutionCommandKind.ScheduleActivity, queued.CommandKind);
    }

    [Fact]
    public async Task CheckpointHandler_DoesNotDispatchSchedulerIntentWhenCheckpointWriteFails()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var activityStateStore = new InMemoryActivityExecutionStateStore();
        await activityStateStore.SaveAsync(NewCompletedActivityState());
        var handler = NewCheckpointHandler(
            activityStateStore,
            new ThrowingCheckpointWriter(),
            queue);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(NewCheckpointWorkItem([NewSchedulerIntent(NewScheduleWorkItem())])).AsTask());

        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task SchedulerPostCommitIntentDispatcher_WrapsKnownSchedulerWorkValidationFailures()
    {
        var dispatcher = new RuntimeSchedulerPostCommitIntentDispatcher(new InMemoryWorkflowSchedulerWorkQueue());
        using var document = JsonDocument.Parse("""
        {
            "workflowExecutionId": "wfexec-1",
            "commandId": "command-1",
            "commandKind": "ScheduleActivity",
            "envelopeId": "envelope-1",
            "idempotencyKey": "idem-1",
            "enqueuedAt": "2026-06-11T12:00:00Z",
            "recordedAt": "2026-06-11T12:00:00Z"
        }
        """);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(NewSchedulerIntent(document.RootElement.Clone(), "idem-1")).AsTask());

        Assert.Contains("payload is not valid scheduler work", exception.Message);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public async Task AddWorkflowRuntime_ContributesSchedulerHandlerAndPreservesQueuedWorkItem()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        var contribution = Assert.Single(services
            .Where(descriptor => descriptor.ServiceType == typeof(RuntimePostCommitIntentHandlerContribution))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RuntimePostCommitIntentHandlerContribution>()
            .Where(candidate => candidate.IntentKind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        Assert.Equal(typeof(RuntimeSchedulerPostCommitIntentDispatcher), contribution.HandlerType);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var expected = NewScheduleWorkItem();

        await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>()
            .DispatchAsync(NewSchedulerIntent(expected));

        var actual = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAllAsync(new RuntimeSchedulerWorkQuery(expected.WorkflowExecutionId)));
        Assert.Equal(expected.WorkItemId, actual.WorkItemId);
        Assert.Equal(expected.WorkflowExecutionId, actual.WorkflowExecutionId);
        Assert.Equal(expected.CommandId, actual.CommandId);
        Assert.Equal(expected.CommandKind, actual.CommandKind);
        Assert.Equal(expected.EnvelopeId, actual.EnvelopeId);
        Assert.Equal(expected.IdempotencyKey, actual.IdempotencyKey);
        Assert.Equal(expected.EnqueuedAt, actual.EnqueuedAt);
        Assert.Equal(expected.RecordedAt, actual.RecordedAt);
        Assert.Equal(expected.Sequence, actual.Sequence);
        Assert.Equal(expected.Payload?.GetRawText(), actual.Payload?.GetRawText());
        Assert.Equal(
            expected.CommandMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.CommandMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(
            expected.EnvelopeMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.EnvelopeMetadata.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    private WorkflowCompleteActivitySchedulerWorkHandler NewCompleteHandler(
        IWorkflowSchedulerWorkQueue queue,
        IWorkflowExecutableStore executableStore) =>
        new(
            new InMemoryActivityExecutionStateStore(),
            queue,
            executableStore,
            new FixedTimeProvider(_now));

    private WorkflowCheckpointSchedulerWorkHandler NewCheckpointHandler(
        IActivityExecutionStateStore activityStateStore,
        IRuntimeCheckpointCommitStore checkpointWriter,
        IWorkflowSchedulerWorkQueue queue,
        IWorkflowExecutionStateStore? workflowExecutionStateStore = null) =>
        new(
            activityStateStore,
            new RuntimeCheckpointCommitter(
                new ImmediateRuntimeCheckpointPersistencePolicy(),
                checkpointWriter),
            inspectionAccumulator: null,
            timeProvider: new FixedTimeProvider(_now),
            workflowExecutionStateStore: workflowExecutionStateStore,
            workflowExecutableStore: _checkpointExecutableStore);

    private RuntimeSchedulerWorkItem NewCompleteWorkItem(
        WorkflowExecutableIdentity pinnedExecutable,
        IReadOnlyCollection<string> outcomeNames,
        SchedulerCompletionKind completionKind) =>
        new(
            workItemId: "work-1",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:complete:actexec-source",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 10,
            payload: JsonSerializer.SerializeToElement(new RuntimeCompleteActivityCommandPayload(
                pinnedExecutable,
                "node-source",
                "actexec-source",
                parentActivityExecutionId: null,
                branchId: "branch-a",
                outcomeNames: outcomeNames,
                reason: CompletionReason(completionKind),
                completionKind: completionKind)),
            commandMetadata: new Dictionary<string, string> { ["source"] = "test" },
            envelopeMetadata: new Dictionary<string, string> { ["transport"] = "in-process" });

    private static string CompletionReason(SchedulerCompletionKind completionKind) =>
        completionKind switch
        {
            SchedulerCompletionKind.ActivityCompleted => RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason,
            SchedulerCompletionKind.ParentCompletionEvaluation => RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            SchedulerCompletionKind.ContinuationScheduling => RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason,
            _ => completionKind.ToString()
        };

    private RuntimeSchedulerWorkItem NewCheckpointWorkItem(
        IReadOnlyCollection<RuntimePostCommitIntent> postCommitIntents,
        string checkpointName = RuntimeCheckpointNames.ActivityCompleted,
        IReadOnlyDictionary<string, string>? commandMetadata = null,
        IReadOnlyCollection<string>? activityExecutionIds = null) =>
        new(
            workItemId: "checkpoint-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-1",
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:checkpoint",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 10,
            payload: JsonSerializer.SerializeToElement(new RuntimeCheckpointCommandPayload(
                NewIdentity(),
                checkpointName,
                activityExecutionIds ?? ["actexec-source"],
                RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason,
                postCommitIntents)),
            commandMetadata: commandMetadata ?? new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private RuntimePostCommitIntent NewSchedulerIntent(RuntimeSchedulerWorkItem workItem) =>
        NewSchedulerIntent(JsonSerializer.SerializeToElement(workItem), workItem.IdempotencyKey);

    private RuntimePostCommitIntent NewSchedulerIntent(JsonElement payload, string idempotencyKey) =>
        new(
            intentId: "intent-schedule-1",
            workflowExecutionId: "wfexec-1",
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: _now,
            activityExecutionId: "actexec-source",
            idempotencyKey: idempotencyKey,
            payload: payload,
            metadata: new Dictionary<string, string>());

    private RuntimeSchedulerWorkItem NewScheduleWorkItem() =>
        new(
            workItemId: "schedule-work",
            workflowExecutionId: "wfexec-1",
            commandId: "command-schedule",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:schedule:node-next",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 12,
            payload: JsonSerializer.SerializeToElement(new RuntimeScheduleActivityCommandPayload(
                NewIdentity(),
                "node-next",
                "actexec-next",
                RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                "actexec-source")),
            commandMetadata: new Dictionary<string, string>
            {
                ["command-origin"] = "scheduler-parity"
            },
            envelopeMetadata: new Dictionary<string, string>
            {
                ["envelope-trace"] = "trace-scheduler-parity"
            });

    private ActivityExecutionState NewCompletedActivityState() =>
        new(
            Execution: new ActivityExecution(
                ActivityExecutionId: "actexec-source",
                WorkflowExecutionId: "wfexec-1",
                ExecutableNodeId: "node-source",
                AuthoredActivityId: "authored-node-source",
                ActivityType: "test/source",
                ActivityTypeVersion: "1.0.0"),
            Status: ActivityExecutionStatus.Completed,
            SubStatus: null,
            ScheduledAt: _now.AddMinutes(-3),
            StartedAt: _now.AddMinutes(-2),
            CompletedAt: _now,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: "branch-a",
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string> nodeIds) =>
        new(
            identity: NewIdentity(),
            rootActivity: ToRootActivity(nodeIds.Select(NewNode).ToArray()),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode ToRootActivity(IReadOnlyCollection<ExecutableNode> nodes)
    {
        var nodeSnapshot = nodes.ToArray();
        if (nodeSnapshot.Length == 1)
            return nodeSnapshot[0];

        return new ExecutableNode(
            executableNodeId: "$root",
            authoredActivityId: "$root",
            activityType: "test/root",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { type = "root" })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot("children", nodeSnapshot)
            ]);
    }

    private static ExecutableNode NewNode(string nodeId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

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
