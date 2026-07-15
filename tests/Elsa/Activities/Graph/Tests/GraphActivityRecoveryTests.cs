using System.Text.Json;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed class GraphActivityRecoveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BoundaryFault_PreservesInnerIncidentAndRecordsStableCausation()
    {
        var exception = new GraphActivityBoundaryFaultException("incident-inner", "child-execution", "child-node");
        var stateStore = new InMemoryActivityExecutionStateStore();
        var incidentStore = new InMemoryIncidentStateStore();
        var commitStore = new InMemoryRuntimeCheckpointCommitStore(activityExecutionStateStore: stateStore, incidentStateStore: incidentStore);
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), commitStore);
        var state = State("outer", null, "outer", ActivityExecutionStatus.Running);
        await stateStore.SaveAsync(state);
        var workItem = WorkItem(WorkflowExecutionCommandKind.InvokeActivity, "fault", executionScopeId: "outer");
        var recorder = new ActivityFaultIncidentRecorder(new FixedTimeProvider(Now));

        await recorder.CommitAsync(new ActivityFaultIncidentRecordRequest(
            committer,
            workItem,
            "outer",
            "outer-node",
            state,
            exception,
            "ParentCompletionFaulted",
            new Dictionary<string, string>(),
            new Dictionary<string, string>()));

        var incident = Assert.Single(await incidentStore.ListBlockingAsync(WorkflowId));
        Assert.Equal("incident-inner", incident.Metadata[RuntimeMetadataKeys.CausalIncidentId]);
        Assert.Equal("child-execution", incident.Metadata[RuntimeMetadataKeys.CausalActivityExecutionId]);
        Assert.Equal("child-node", incident.Metadata[RuntimeMetadataKeys.CausalExecutableNodeId]);
        Assert.Equal("graph-descendant-fault", incident.Metadata[RuntimeMetadataKeys.CausationKind]);
        Assert.Equal("incident-inner", exception.CausalIncidentId);
    }

    [Fact]
    public async Task CancellationWins_AtomicallyCleansDescendantResourcesAndFencesQueuedScheduling()
    {
        await using var provider = Provider();
        var states = provider.GetRequiredService<IActivityExecutionStateStore>();
        var bookmarks = provider.GetRequiredService<IBookmarkStateStore>();
        var timers = provider.GetRequiredService<IDurableTimerStore>();
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        await states.SaveAsync(State("outer", null, "outer", ActivityExecutionStatus.Suspended));
        await states.SaveAsync(State("child", "outer", "outer", ActivityExecutionStatus.Suspended));
        await bookmarks.SaveAsync(new BookmarkState(
            "bookmark-child", WorkflowId, "child", "child-node", "resume", "timer", "hash", null,
            new Dictionary<string, string>(), Now, null));
        await timers.SaveAsync(new DurableTimer("timer-child", WorkflowId, "timer", "hash", Now.AddHours(1), Now));
        var pending = WorkItem(WorkflowExecutionCommandKind.ScheduleActivity, "pending", executionScopeId: "outer");
        await queue.EnqueueAsync(pending);

        var handler = provider.GetServices<IWorkflowSchedulerWorkHandler>()
            .Single(item => item is WorkflowCancelActivityScopeSchedulerWorkHandler);
        await handler.HandleAsync(CancelWorkItem());

        Assert.Equal(ActivityExecutionStatus.Cancelled, (await states.FindAsync(WorkflowId, "outer"))!.Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await states.FindAsync(WorkflowId, "child"))!.Status);
        Assert.Null(await bookmarks.FindAsync(WorkflowId, "bookmark-child"));
        Assert.Null(await timers.FindAsync(WorkflowId, "timer-child"));
        Assert.DoesNotContain(await queue.ListAsync(new RuntimeSchedulerWorkQuery(WorkflowId)), item => item.WorkItemId == pending.WorkItemId);

        var identity = Identity();
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(Executable(identity));
        var latePayload = new RuntimeScheduleActivityCommandPayload(
            identity,
            "outer-node",
            "late-child",
            "late-resume",
            "outer",
            "outer",
            ActivitySchedulingProvenance.From(WorkflowId, "outer", "outer", null, null, null, "outer", "late-resume"));
        var lateWork = WorkItem(
            WorkflowExecutionCommandKind.ScheduleActivity,
            "late-schedule",
            JsonSerializer.SerializeToElement(latePayload),
            "outer");
        var scheduleHandler = provider.GetServices<IWorkflowSchedulerWorkHandler>()
            .Single(item => item is WorkflowScheduleActivitySchedulerWorkHandler);
        await scheduleHandler.HandleAsync(lateWork);
        Assert.Null(await states.FindAsync(WorkflowId, "late-child"));

        var commit = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Single(commit.Commit.StateChanges.ActivityScopeCleanups);
        Assert.Equal(2, commit.Commit.StateChanges.ActivityExecutions.Count);
    }

    [Fact]
    public async Task CompletionWins_CancellationCannotOverwriteTheTerminalBoundary()
    {
        await using var provider = Provider();
        var states = provider.GetRequiredService<IActivityExecutionStateStore>();
        await states.SaveAsync(State("outer", null, "outer", ActivityExecutionStatus.Completed));

        var handler = provider.GetServices<IWorkflowSchedulerWorkHandler>()
            .Single(item => item is WorkflowCancelActivityScopeSchedulerWorkHandler);
        await handler.HandleAsync(CancelWorkItem());

        Assert.Equal(ActivityExecutionStatus.Completed, (await states.FindAsync(WorkflowId, "outer"))!.Status);
        Assert.Empty(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
    }

    [Fact]
    public async Task CancellationTraversal_IsIterativeForDeepNestedScopes()
    {
        await using var provider = Provider();
        var states = provider.GetRequiredService<IActivityExecutionStateStore>();
        const int depth = 2_000;
        string? parent = null;
        for (var index = 0; index < depth; index++)
        {
            var id = index == 0 ? "outer" : $"child-{index}";
            await states.SaveAsync(State(id, parent, index == 0 ? "outer" : id, ActivityExecutionStatus.Running));
            parent = id;
        }

        var handler = provider.GetServices<IWorkflowSchedulerWorkHandler>()
            .Single(item => item is WorkflowCancelActivityScopeSchedulerWorkHandler);
        await handler.HandleAsync(CancelWorkItem());

        Assert.Equal(depth, (await states.ListAsync(WorkflowId)).Count(state => state.Status == ActivityExecutionStatus.Cancelled));
    }

    [Fact]
    public async Task CancellationTraversal_TerminatesAndCancelsEachExecutionOnceWhenStoredParentsContainACycle()
    {
        await using var provider = Provider();
        var states = provider.GetRequiredService<IActivityExecutionStateStore>();
        await states.SaveAsync(State("outer", "child", "outer", ActivityExecutionStatus.Running));
        await states.SaveAsync(State("child", "outer", "child", ActivityExecutionStatus.Running));

        var handler = provider.GetServices<IWorkflowSchedulerWorkHandler>()
            .Single(item => item is WorkflowCancelActivityScopeSchedulerWorkHandler);
        await handler.HandleAsync(CancelWorkItem());

        var cancelled = await states.ListAsync(WorkflowId);
        Assert.Equal(2, cancelled.Count);
        Assert.All(cancelled, state => Assert.Equal(ActivityExecutionStatus.Cancelled, state.Status));
        var commit = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(2, commit.Commit.StateChanges.ActivityExecutions.Count);
    }

    [Fact]
    public async Task Retry_CreatesFreshOuterScopeWithExactEncodedInputsAndAttemptLineage()
    {
        await using var provider = Provider();
        var states = provider.GetRequiredService<IActivityExecutionStateStore>();
        var values = provider.GetRequiredService<IDurableValueStateStore>();
        var artifacts = provider.GetRequiredService<IWorkflowExecutableStore>();
        var identity = Identity();
        await artifacts.SaveAsync(Executable(identity));
        var prior = State("outer", "parent", "outer", ActivityExecutionStatus.Faulted) with
        {
            Attempt = new ActivityExecutionAttemptLineage(2, "outer-first", "outer-first"),
            Provenance = ActivitySchedulingProvenance.From(
                WorkflowId, "parent", "parent", null, null, null, "outer", "graph-entry",
                new Dictionary<string, string>(), new ActivityExecutionAttemptLineage(2, "outer-first", "outer-first")),
            Metadata = PinnedMetadata(identity)
        };
        await states.SaveAsync(prior);
        var input = InputValue("outer", "order", "Order", JsonSerializer.SerializeToElement(new { id = 42 }));
        await values.SaveAsync(input);

        var handler = provider.GetServices<IWorkflowSchedulerWorkHandler>()
            .Single(item => item is WorkflowRetryActivityBoundarySchedulerWorkHandler);
        await handler.HandleAsync(RetryWorkItem(identity));
        await handler.HandleAsync(RetryWorkItem(identity));

        var cloned = Assert.Single((await values.ListAsync(WorkflowId)), value =>
            StringComparer.Ordinal.Equals(value.SourceActivityExecutionId, "outer-retry"));
        Assert.Equal(ActivityExecutionScopeKeys.Input("outer-retry", "order"), cloned.DurableValueId);
        Assert.True(JsonElement.DeepEquals(input.InlineValue!.Value, cloned.InlineValue!.Value));
        Assert.Equal("outer", cloned.Metadata[RuntimeMetadataKeys.RetrySourceActivityExecutionId]);

        var retryCommit = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        var intent = Assert.Single(retryCommit.Commit.PostCommitIntents);
        var scheduled = intent.Payload!.Value.Deserialize<RuntimeSchedulerWorkItem>()!;
        Assert.Equal("outer-retry", scheduled.ExecutionScopeId);
        Assert.Equal(3, scheduled.Attempt!.AttemptNumber);
        Assert.Equal("outer-first", scheduled.Attempt.FirstAttemptActivityExecutionId);
        Assert.Equal("outer", scheduled.Attempt.PreviousAttemptActivityExecutionId);
        var payload = scheduled.Payload!.Value.Deserialize<RuntimeScheduleActivityCommandPayload>()!;
        Assert.Equal(identity.ArtifactHash, payload.PinnedExecutable.ArtifactHash);
        Assert.Equal("outer-retry", payload.ActivityExecutionId);

        var materializer = provider.GetRequiredService<IRuntimeActivityInputMaterializer>();
        var outputReader = Assert.IsAssignableFrom<IRuntimeActivityOutputReader>(
            provider.GetRequiredService<IRuntimeActivityOutputRegister>());
        var materialized = await materializer.MaterializeInputsAsync(
            Executable(identity).RootActivity,
            new RuntimeInputBindingResolutionContext(
                WorkflowId,
                "outer-retry",
                new Dictionary<string, DurableValueState> { [cloned.ValueId] = cloned },
                outputReader,
                provider));
        var effectiveInput = Assert.Single(materialized);
        Assert.Equal("Order", effectiveInput.Name);
        Assert.Equal(42, Assert.IsType<JsonElement>(effectiveInput.Value).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Retry_RejectsARequestedExecutionIdOwnedByAnotherAttempt()
    {
        await using var provider = Provider();
        var states = provider.GetRequiredService<IActivityExecutionStateStore>();
        var identity = Identity();
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(Executable(identity));
        await states.SaveAsync(State("outer", "parent", "outer", ActivityExecutionStatus.Faulted) with
        {
            Metadata = PinnedMetadata(identity)
        });
        await states.SaveAsync(State("outer-retry", "different-parent", "outer-retry", ActivityExecutionStatus.Scheduled) with
        {
            Metadata = PinnedMetadata(identity)
        });

        var handler = provider.GetServices<IWorkflowSchedulerWorkHandler>()
            .Single(item => item is WorkflowRetryActivityBoundarySchedulerWorkHandler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(RetryWorkItem(identity)).AsTask());
        Assert.Contains("already used by a different execution attempt", exception.Message, StringComparison.Ordinal);
        Assert.Empty(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        return services.BuildServiceProvider();
    }

    private static RuntimeSchedulerWorkItem CancelWorkItem() => WorkItem(
        WorkflowExecutionCommandKind.CancelActivityScope,
        "cancel",
        JsonSerializer.SerializeToElement(new CancelActivityScopeCommand("outer", "outer", "operator-request")),
        "outer");

    private static RuntimeSchedulerWorkItem RetryWorkItem(WorkflowExecutableIdentity identity) => WorkItem(
        WorkflowExecutionCommandKind.RetryActivityBoundary,
        "retry",
        JsonSerializer.SerializeToElement(new RetryActivityBoundaryCommand("outer", "outer-retry", identity, "policy-retry")),
        "outer");

    private static RuntimeSchedulerWorkItem WorkItem(
        WorkflowExecutionCommandKind kind,
        string id,
        JsonElement? payload = null,
        string? executionScopeId = null) => new(
        $"work-{id}", WorkflowId, $"command-{id}", kind, "envelope", $"key-{id}", Now, Now, 1,
        payload, new Dictionary<string, string>(), new Dictionary<string, string>(), executionScopeId);

    private static ActivityExecutionState State(
        string id,
        string? parentId,
        string executionScopeId,
        ActivityExecutionStatus status) => new(
        new ActivityExecution(id, WorkflowId, id == "outer" ? "outer-node" : $"node-{id}", $"authored-{id}", "activity", "1"),
        status,
        null,
        1,
        Now,
        Now,
        status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted ? Now : null,
        parentId,
        parentId,
        null,
        null,
        ActivitySchedulingProvenance.From(WorkflowId, parentId, parentId, null, null, null, executionScopeId, "test"),
        null,
        [],
        [],
        status == ActivityExecutionStatus.Faulted ? 1 : 0,
        status == ActivityExecutionStatus.Faulted ? 1 : 0,
        new Dictionary<string, string>(),
        executionScopeId,
        new ActivityExecutionAttemptLineage(1, id, null));

    private static DurableValueState InputValue(string executionId, string referenceKey, string inputName, JsonElement value)
    {
        var id = ActivityExecutionScopeKeys.Input(executionId, referenceKey);
        return new DurableValueState(
            id, WorkflowId, id, new RuntimeValueTypeDescriptor("object", "elsa.json", JsonSerializer.SerializeToElement(new { alias = "object" })),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline, value, null, executionId, Now,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.BoundaryExecutionScopeId] = executionId,
                [RuntimeMetadataKeys.BoundaryValueRole] = "input",
                [RuntimeMetadataKeys.BoundaryReferenceKey] = referenceKey,
                [RuntimeMetadataKeys.BoundaryInputName] = inputName,
                ["runtime.storageDriverKey"] = "elsa.json"
            });
    }

    private static WorkflowExecutable Executable(WorkflowExecutableIdentity identity)
    {
        var root = new ExecutableNode(
            "outer-node", "authored-outer", "graph", "1",
            new Elsa.Activities.Runtime.Core.Models.RuntimeActivityDescriptor("elsa.graph-activity", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>());
        return new WorkflowExecutable(identity, root, new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>());
    }

    private static WorkflowExecutableIdentity Identity() => new("artifact", "definition", "version-id", "1.0.0", "sha256:artifact");

    private static IReadOnlyDictionary<string, string> PinnedMetadata(WorkflowExecutableIdentity identity) =>
        new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.PinnedArtifactId] = identity.ArtifactId,
            [RuntimeMetadataKeys.PinnedArtifactVersion] = identity.ArtifactVersion,
            [RuntimeMetadataKeys.PinnedArtifactHash] = identity.ArtifactHash
        };

    private const string WorkflowId = "workflow-execution";

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

}
