using Elsa.Workflows.Runtime.Api;
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
/// ADR 0029 Move 2 (first slice, slot-invoked handler model): the workflow <c>Invoke</c> slot runs the handler before
/// <c>next</c> and the <c>Checkpoint</c> slot persists the commit it staged. Covers the middleware, the Cancel handler's
/// staging vs inline commit, the end-to-end feature path, the Invoke-before-Checkpoint ordering, and the fail-loud guard
/// when the Invoke slot is missing. Cancel is the only handler migrated in this slice; behaviour is preserved.
/// </summary>
public sealed class RuntimeCheckpointSlotDecompositionTests : RuntimePipelineTestSupport
{
    [Fact]
    public async Task CheckpointMiddleware_CommitsStagedCommit()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var middleware = new RuntimeWorkflowCheckpointMiddleware(NewCommitter(store));
        var context = new WorkflowRuntimePipelineContext(NewCancelWorkItem())
        {
            Workspace = { PendingCheckpointCommit = NewCommit() }
        };

        await middleware.InvokeAsync(context, _ => ValueTask.CompletedTask);

        Assert.Single(store.ListCommits());
    }

    [Fact]
    public async Task CheckpointMiddleware_DoesNothing_WhenNoCommitStaged()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var middleware = new RuntimeWorkflowCheckpointMiddleware(NewCommitter(store));
        var context = new WorkflowRuntimePipelineContext(NewCancelWorkItem());

        await middleware.InvokeAsync(context, _ => ValueTask.CompletedTask);

        Assert.Empty(store.ListCommits());
    }

    [Fact]
    public async Task CancelHandler_StagesCommitForTheCheckpointSlot_WhenDispatchedThroughThePipeline()
    {
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore();
        var handler = await NewSeededCancelHandler(checkpointStore);
        var context = new WorkflowRuntimePipelineContext(NewCancelWorkItem());

        // The Invoke slot calls the context-aware overload; it stages the commit rather than committing inline.
        await handler.HandleAsync(NewCancelWorkItem(), context);

        Assert.Empty(checkpointStore.ListCommits());
        var staged = context.Workspace.PendingCheckpointCommit;
        Assert.NotNull(staged);
        Assert.Equal(RuntimeCheckpointNames.ActivityCancelled, staged.Checkpoint.Name);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, staged.StateChanges.WorkflowExecution!.State.Status);
    }

    [Fact]
    public async Task CancelHandler_CommitsInline_OnDirectDispatch()
    {
        // Behaviour-preserving fallback: dispatched without a pipeline (plain IWorkflowSchedulerWorkHandler), it commits.
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore();
        var handler = await NewSeededCancelHandler(checkpointStore);

        await handler.HandleAsync(NewCancelWorkItem());

        Assert.Single(checkpointStore.ListCommits());
    }

    [Fact]
    public async Task Cancel_ThroughFeaturePipeline_CommitsViaCheckpointSlot()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewExecutable());
        await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewActivityStateForStatus(ActivityExecutionStatus.Running));
        var dispatcher = provider.GetRequiredService<IRuntimeExecutionPipelineDispatcher>();
        var cancelHandler = provider.GetServices<IWorkflowSchedulerWorkHandler>().OfType<WorkflowCancelSchedulerWorkHandler>().Single();

        await dispatcher.DispatchAsync(NewCancelWorkItem(), cancelHandler);

        var committed = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityCancelled, committed.Commit.Checkpoint.Name);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, committed.Commit.StateChanges.WorkflowExecution!.State.Status);
    }

    [Fact]
    public void BuildPlan_OrdersTheInvokeSlotBeforeTheCheckpointSlot()
    {
        var steps = new WorkflowRuntimePipelineBuilder().BuildPlan().Steps;

        var invokeIndex = IndexOf(steps, typeof(RuntimeWorkflowInvokeMiddleware));
        var checkpointIndex = IndexOf(steps, typeof(RuntimeWorkflowCheckpointMiddleware));
        Assert.True(invokeIndex >= 0 && checkpointIndex >= 0);
        Assert.True(invokeIndex < checkpointIndex, "The Invoke slot must run before the Checkpoint slot so the staged commit exists when Checkpoint commits.");
    }

    [Fact]
    public async Task Dispatch_FailsLoudly_WhenTheInvokeSlotIsMissingFromThePlan()
    {
        // The dispatcher stages the handler + a no-op terminal; without the Invoke slot the handler would silently never
        // run. The terminal guard turns that misconfiguration into a hard error instead of a success-that-did-nothing.
        await using var provider = BuildWorkflowOnlyProvider();
        var workflowPlan = new WorkflowRuntimePipelineBuilder().Remove<RuntimeWorkflowInvokeMiddleware>().BuildPlan();
        var dispatcher = new RuntimeExecutionPipelineDispatcher(
            new RuntimeSchedulerPipelineSelector(),
            new RuntimeWorkflowExecutionPipeline(workflowPlan, provider),
            new PassThroughActivityPipeline());
        var handler = NewCancelHandler(
            new InMemoryWorkflowExecutionStateStore(),
            new InMemoryActivityExecutionStateStore(),
            new InMemoryRuntimeCheckpointCommitStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(NewCancelWorkItem(), handler).AsTask());
        Assert.Contains(RuntimeWorkflowPipelineSlots.Invoke, exception.Message);
    }

    private async Task<WorkflowCancelSchedulerWorkHandler> NewSeededCancelHandler(InMemoryRuntimeCheckpointCommitStore checkpointStore)
    {
        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        await workflowStore.SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        await activityStore.SaveAsync(NewActivityStateForStatus(ActivityExecutionStatus.Running));
        return NewCancelHandler(workflowStore, activityStore, checkpointStore);
    }

    private static WorkflowCancelSchedulerWorkHandler NewCancelHandler(
        IWorkflowExecutionStateStore workflowStore,
        IActivityExecutionStateStore activityStore,
        IRuntimeCheckpointCommitStore checkpointStore) =>
        new(
            workflowStore,
            activityStore,
            NewCommitter(checkpointStore),
            new RuntimeActivityExecutionInspectionAccumulator(new InMemoryActivityExecutionInspectionStore()),
            TimeProvider.System);

    private static RuntimeCheckpointCommitter NewCommitter(IRuntimeCheckpointCommitStore store) =>
        new(new ImmediateRuntimeCheckpointPersistencePolicy(), store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);

    private static int IndexOf(IReadOnlyList<RuntimePipelinePlanStep> steps, Type middlewareType)
    {
        for (var index = 0; index < steps.Count; index++)
            if (steps[index].MiddlewareType == middlewareType)
                return index;
        return -1;
    }

    // Resolves the workflow built-ins that remain after removing the Invoke slot (LoadState/Scheduling/Checkpoint/PostCommit + committer).
    private static ServiceProvider BuildWorkflowOnlyProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeExecutionOwnershipContextAccessor, AsyncLocalRuntimeExecutionOwnershipContextAccessor>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<RuntimeWorkflowLoadStateMiddleware>();
        services.AddSingleton<RuntimeWorkflowSchedulingMiddleware>();
        services.AddSingleton<RuntimeWorkflowCheckpointMiddleware>();
        services.AddSingleton<RuntimeWorkflowPostCommitMiddleware>();
        return services.BuildServiceProvider();
    }

    private static RuntimeCheckpointCommit NewCommit() =>
        new(
            CommitId: "commit:test",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: "checkpoint:test",
                Name: RuntimeCheckpointNames.ActivityCancelled,
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

    private sealed class PassThroughActivityPipeline : IRuntimeActivityExecutionPipeline
    {
        public RuntimePipelinePlan Plan => new ActivityRuntimePipelineBuilder().BuildPlan();

        public ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate terminal) => terminal(context);
    }
}
