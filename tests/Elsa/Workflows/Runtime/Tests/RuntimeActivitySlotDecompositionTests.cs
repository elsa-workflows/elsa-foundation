using Elsa.Workflows.Runtime.Core.Builders;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// ADR 0029 Move 2 (activity pipeline slot-invoked model): the activity <c>Invoke</c> slot runs the handler before
/// <c>next</c> and the <c>Checkpoint</c> slot drains the commits it staged. Covers the Invoke/Checkpoint middleware, the
/// slot ordering, the fail-loud guard when the Invoke slot is missing, and — the control-room hard condition — that the
/// Checkpoint slot drains N staged commits as N committer calls in stage order, never folded/batched into one.
/// No activity handler is migrated in this slice; handlers still commit inline in the Invoke slot (behaviour preserved).
/// </summary>
public sealed class RuntimeActivitySlotDecompositionTests : RuntimePipelineTestSupport
{
    [Fact]
    public async Task InvokeMiddleware_RunsStagedHandler_ThenClearsIt()
    {
        var ran = false;
        var middleware = new RuntimeActivityInvokeMiddleware();
        var context = new ActivityRuntimePipelineContext(NewWorkItem(WorkflowExecutionCommandKind.ScheduleActivity))
        {
            Workspace = { InvokeHandler = _ => { ran = true; return ValueTask.CompletedTask; } }
        };

        await middleware.InvokeAsync(context, _ => ValueTask.CompletedTask);

        Assert.True(ran);
        Assert.Null(context.Workspace.InvokeHandler);
    }

    [Fact]
    public async Task CheckpointMiddleware_DrainsStagedCommitsInOrderIndividually()
    {
        // Control-room hard condition (2): one staged entry == one committer call, in stage order, never folded.
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var middleware = new RuntimeActivityCheckpointMiddleware(NewCommitter(store));
        var context = new ActivityRuntimePipelineContext(NewWorkItem(WorkflowExecutionCommandKind.ScheduleActivity));
        context.Workspace.StageCheckpointCommit(NewCommit("commit:1"));
        context.Workspace.StageCheckpointCommit(NewCommit("commit:2"));
        context.Workspace.StageCheckpointCommit(NewCommit("commit:3"));

        await middleware.InvokeAsync(context, _ => ValueTask.CompletedTask);

        // Three staged entries produce three committed records (not one folded commit)...
        var committedIds = store.ListCommits().Select(record => record.Commit.CommitId).ToArray();
        Assert.Equal(3, committedIds.Length);
        Assert.Equal<IEnumerable<string>>(new[] { "commit:1", "commit:2", "commit:3" }, committedIds);
    }

    [Fact]
    public async Task CheckpointMiddleware_DoesNothing_WhenNoCommitStaged()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var middleware = new RuntimeActivityCheckpointMiddleware(NewCommitter(store));
        var context = new ActivityRuntimePipelineContext(NewWorkItem(WorkflowExecutionCommandKind.ScheduleActivity));

        await middleware.InvokeAsync(context, _ => ValueTask.CompletedTask);

        Assert.Empty(store.ListCommits());
    }

    [Fact]
    public void Workspace_PendingCheckpointCommitConvenience_TracksTheOrderedList()
    {
        var workspace = new RuntimePipelineWorkspace();
        Assert.Null(workspace.PendingCheckpointCommit);
        Assert.Empty(workspace.PendingCheckpointCommits);

        // Setting the convenience property stages exactly one commit (clearing any prior list).
        var single = NewCommit("commit:single");
        workspace.PendingCheckpointCommit = single;
        Assert.Same(single, workspace.PendingCheckpointCommit);
        Assert.Same(single, Assert.Single(workspace.PendingCheckpointCommits));

        // Appending keeps stage order; the convenience getter returns the last staged commit.
        var second = NewCommit("commit:second");
        workspace.StageCheckpointCommit(second);
        Assert.Equal<IEnumerable<RuntimeCheckpointCommit>>(new[] { single, second }, workspace.PendingCheckpointCommits);
        Assert.Same(second, workspace.PendingCheckpointCommit);

        // Setting to null clears the whole list.
        workspace.PendingCheckpointCommit = null;
        Assert.Empty(workspace.PendingCheckpointCommits);
    }

    [Fact]
    public void BuildPlan_OrdersTheActivityInvokeSlotBeforeTheCheckpointSlot()
    {
        var steps = new ActivityRuntimePipelineBuilder().BuildPlan().Steps;

        var invokeIndex = IndexOf(steps, typeof(RuntimeActivityInvokeMiddleware));
        var checkpointIndex = IndexOf(steps, typeof(RuntimeActivityCheckpointMiddleware));
        Assert.True(invokeIndex >= 0 && checkpointIndex >= 0);
        Assert.True(invokeIndex < checkpointIndex, "The Invoke slot must run before the Checkpoint slot so staged commits exist when Checkpoint drains them.");
    }

    [Fact]
    public async Task Dispatch_RunsActivityHandlerThroughTheInvokeSlot()
    {
        // Behaviour-preserving: a plain (un-migrated) activity handler runs exactly once via the Invoke slot; the
        // Checkpoint slot drains an empty list, so nothing changes versus the former terminal-dispatch model.
        await using var provider = BuildActivityProvider();
        var dispatcher = new RuntimeExecutionPipelineDispatcher(
            new RuntimeSchedulerPipelineSelector(),
            new PassThroughWorkflowPipeline(),
            new RuntimeActivityExecutionPipeline(new ActivityRuntimePipelineBuilder().BuildPlan(), provider));
        var handler = new RecordingHandler();

        await dispatcher.DispatchAsync(NewWorkItem(WorkflowExecutionCommandKind.ScheduleActivity), handler);

        Assert.Single(handler.WorkItemIds);
    }

    [Fact]
    public async Task Dispatch_FailsLoudly_WhenTheActivityInvokeSlotIsMissingFromThePlan()
    {
        await using var provider = BuildActivityProvider();
        var activityPlan = new ActivityRuntimePipelineBuilder().Remove<RuntimeActivityInvokeMiddleware>().BuildPlan();
        var dispatcher = new RuntimeExecutionPipelineDispatcher(
            new RuntimeSchedulerPipelineSelector(),
            new PassThroughWorkflowPipeline(),
            new RuntimeActivityExecutionPipeline(activityPlan, provider));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(NewWorkItem(WorkflowExecutionCommandKind.ScheduleActivity), new RecordingHandler()).AsTask());
        Assert.Contains(RuntimeActivityPipelineSlots.Invoke, exception.Message);
    }

    [Fact]
    public async Task Dispatch_StagesAmbientServicesOnTheWorkspace_ForPipelineHandlersToReadExplicitly()
    {
        // RT-7: the drain's ambient services flow explicitly through the dispatcher onto the workspace, replacing the
        // former AsyncLocal service locator. A pipeline-aware handler reads them from the context it is handed.
        await using var provider = BuildActivityProvider();
        var dispatcher = new RuntimeExecutionPipelineDispatcher(
            new RuntimeSchedulerPipelineSelector(),
            new PassThroughWorkflowPipeline(),
            new RuntimeActivityExecutionPipeline(new ActivityRuntimePipelineBuilder().BuildPlan(), provider));
        var ambientServices = new ServiceCollection().BuildServiceProvider();
        var handler = new AmbientCapturingHandler();

        await dispatcher.DispatchAsync(NewWorkItem(WorkflowExecutionCommandKind.ScheduleActivity), handler, ambientServices);

        Assert.Same(ambientServices, handler.ObservedAmbientServices);
    }

    private sealed class AmbientCapturingHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
    {
        public string Name => nameof(AmbientCapturingHandler);
        public IServiceProvider? ObservedAmbientServices { get; private set; }

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
        {
            ObservedAmbientServices = pipelineContext.Workspace.AmbientServices;
            return ValueTask.CompletedTask;
        }
    }

    private static RuntimeCheckpointCommitter NewCommitter(IRuntimeCheckpointCommitStore store) =>
        new(new ImmediateRuntimeCheckpointPersistencePolicy(), store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);

    private static int IndexOf(IReadOnlyList<RuntimePipelinePlanStep> steps, Type middlewareType)
    {
        for (var index = 0; index < steps.Count; index++)
            if (steps[index].MiddlewareType == middlewareType)
                return index;
        return -1;
    }

    // Resolves the activity built-ins (LoadState/InputEvaluation/Invoke/OutputCapture/Scheduling/Checkpoint/PostCommit + committer).
    private static ServiceProvider BuildActivityProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeExecutionOwnershipContextAccessor, AsyncLocalRuntimeExecutionOwnershipContextAccessor>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<RuntimeActivityLoadStateMiddleware>();
        services.AddSingleton<RuntimeActivityInputEvaluationMiddleware>();
        services.AddSingleton<RuntimeActivityInvokeMiddleware>();
        services.AddSingleton<RuntimeActivityOutputCaptureMiddleware>();
        services.AddSingleton<RuntimeActivitySchedulingMiddleware>();
        services.AddSingleton<RuntimeActivityCheckpointMiddleware>();
        services.AddSingleton<RuntimeActivityPostCommitMiddleware>();
        return services.BuildServiceProvider();
    }

    private static RuntimeCheckpointCommit NewCommit(string commitId) =>
        new(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{commitId}",
                Name: RuntimeCheckpointNames.ActivityScheduled,
                WorkflowExecutionId: "wf-1",
                OccurredAt: DateTimeOffset.UnixEpoch,
                ActivityExecutionIds: [],
                Metadata: new Dictionary<string, string>()),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: []),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>());

    private sealed class PassThroughWorkflowPipeline : IRuntimeWorkflowExecutionPipeline
    {
        public RuntimePipelinePlan Plan => new WorkflowRuntimePipelineBuilder().BuildPlan();

        public ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate terminal) => terminal(context);
    }
}
