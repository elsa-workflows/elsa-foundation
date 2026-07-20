using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Builders;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// MS-9 acceptance: with the ActivitySource tracer wired into the engine, a real activity dispatch through the runtime
/// pipeline emits a coherent span tree — drain → dispatch → (activity.execute + checkpoint.commit) — observed through a
/// bare <see cref="ActivityListener"/>, and emits nothing at all when no listener is attached (zero-overhead default).
/// </summary>
public sealed class RuntimeEngineTracingTests : RuntimePipelineTestSupport
{
    [Fact]
    public async Task TracedActivityDispatch_ProducesCoherentSpanTree_AcrossDrainDispatchInvokeAndCommit()
    {
        using var activitySource = new ActivitySource(WorkflowEngineTelemetry.ActivitySourceName);
        var tracer = new ActivitySourceWorkflowEngineTracer(activitySource);

        var recorded = new List<System.Diagnostics.Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkflowEngineTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = recorded.Add
        };
        ActivitySource.AddActivityListener(listener);

        await using var provider = BuildTracedProvider(tracer);
        var drainer = BuildDrainer(provider, tracer, out var queue, out _);
        await queue.EnqueueAsync(NewWorkItem(WorkflowExecutionCommandKind.InvokeActivity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(result.Items).Status);

        var drain = Assert.Single(recorded, activity => activity.OperationName == WorkflowEngineTelemetry.DrainSpanName);
        var dispatch = Assert.Single(recorded, activity => activity.OperationName == WorkflowEngineTelemetry.DispatchSpanName);
        var activityExecute = Assert.Single(recorded, activity => activity.OperationName == WorkflowEngineTelemetry.ActivityExecuteSpanName);
        var commit = Assert.Single(recorded, activity => activity.OperationName == WorkflowEngineTelemetry.CheckpointCommitSpanName);

        // Coherent tree: drain is the root; dispatch nests under drain; both the Invoke-slot and Checkpoint-slot spans
        // nest under dispatch (siblings), matching the pipeline slot order.
        Assert.Null(drain.Parent);
        Assert.Equal(drain.Id, dispatch.ParentId);
        Assert.Equal(dispatch.Id, activityExecute.ParentId);
        Assert.Equal(dispatch.Id, commit.ParentId);
        Assert.Equal(drain.TraceId, commit.TraceId);

        // Stable attribute conventions (identifiers/kinds/counts only — never payloads).
        Assert.Equal(true, drain.GetTagItem(WorkflowEngineTelemetry.DrainOutermostTag));
        Assert.Equal("wfexec-1", drain.GetTagItem(WorkflowEngineTelemetry.WorkflowExecutionIdTag));
        Assert.Equal("work-1", dispatch.GetTagItem(WorkflowEngineTelemetry.WorkItemIdTag));
        Assert.Equal(WorkflowExecutionCommandKind.InvokeActivity.ToString(), dispatch.GetTagItem(WorkflowEngineTelemetry.CommandKindTag));
        Assert.Equal(nameof(StagingHandler), dispatch.GetTagItem(WorkflowEngineTelemetry.HandlerNameTag));
        Assert.Equal(WorkflowEngineTelemetry.OutcomeCompleted, dispatch.GetTagItem(WorkflowEngineTelemetry.OutcomeTag));
        Assert.Equal("cp-1", commit.GetTagItem(WorkflowEngineTelemetry.CheckpointIdTag));
        Assert.Equal(1, drain.GetTagItem(WorkflowEngineTelemetry.DrainItemsProcessedTag));
    }

    [Fact]
    public async Task TracedDispatch_EmitsNoSpans_WhenNoListenerIsAttached()
    {
        // Zero-overhead default: the ActivitySource tracer is wired, but with no listener sampling the source every
        // Start* returns null, so a full dispatch runs without producing any Activity.
        using var activitySource = new ActivitySource(WorkflowEngineTelemetry.ActivitySourceName);
        var tracer = new ActivitySourceWorkflowEngineTracer(activitySource);

        await using var provider = BuildTracedProvider(tracer);
        var drainer = BuildDrainer(provider, tracer, out var queue, out _);
        await queue.EnqueueAsync(NewWorkItem(WorkflowExecutionCommandKind.InvokeActivity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(result.Items).Status);
        Assert.Null(System.Diagnostics.Activity.Current);
    }

    [Fact]
    public void StartDrainCycle_TagsOnlyTheOutermostDrain_EvenWhenAHostSpanIsItsDirectParent()
    {
        // The engine declares "outermost" at drain start (it knows nesting); listeners must never have to re-derive it
        // from parent ids, which fail on the synchronous execute path where the ASP.NET Core request activity is the
        // drain span's direct parent.
        using var engineSource = new ActivitySource(WorkflowEngineTelemetry.ActivitySourceName);
        var tracer = new ActivitySourceWorkflowEngineTracer(engineSource);
        using var hostSource = new ActivitySource("Test.Host.Request");

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkflowEngineTelemetry.ActivitySourceName || source.Name == "Test.Host.Request",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        // Simulates the ASP.NET Core request activity wrapping a synchronous execute.
        using var request = hostSource.StartActivity("HTTP POST /execute");
        Assert.NotNull(request);

        using (var outer = tracer.StartDrainCycle(new("wfexec-parent")))
        {
            Assert.NotNull(outer);
            Assert.Equal(true, outer!.GetTagItem(WorkflowEngineTelemetry.DrainOutermostTag));

            // A child workflow's drain nested inside the parent's (ChildStartExecutor) carries no outermost tag.
            using var nested = tracer.StartDrainCycle(new("wfexec-child"));
            Assert.NotNull(nested);
            Assert.Null(nested!.GetTagItem(WorkflowEngineTelemetry.DrainOutermostTag));
        }

        // A second sequential cycle of a multi-cycle drain is outermost again.
        using var secondCycle = tracer.StartDrainCycle(new("wfexec-parent"));
        Assert.Equal(true, secondCycle!.GetTagItem(WorkflowEngineTelemetry.DrainOutermostTag));
    }

    private WorkflowSchedulerDrainer BuildDrainer(
        IServiceProvider provider,
        IWorkflowEngineTracer tracer,
        out InMemoryWorkflowSchedulerWorkQueue queue,
        out StagingHandler handler)
    {
        queue = new InMemoryWorkflowSchedulerWorkQueue();
        handler = new StagingHandler();
        var dispatcher = new RuntimeExecutionPipelineDispatcher(
            new RuntimeSchedulerPipelineSelector(),
            new RuntimeWorkflowExecutionPipeline(new WorkflowRuntimePipelineBuilder().BuildPlan(), provider),
            new RuntimeActivityExecutionPipeline(new ActivityRuntimePipelineBuilder().BuildPlan(), provider));

        return new WorkflowSchedulerDrainer(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new InMemoryWorkflowExecutionStateStore(),
            new FixedTimeProvider(Now),
            pauseGate: null,
            pipelineDispatcher: dispatcher,
            faultCapturePolicy: null,
            poisonStore: null,
            retryPolicy: null,
            tracer: tracer);
    }

    private static ServiceProvider BuildTracedProvider(IWorkflowEngineTracer tracer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tracer);

        // The Checkpoint slot middleware commits through the committer (Move 2); the committer resolves the tracer so
        // its span nests under the dispatch span.
        services.AddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        // Mirror production's committer activation: AddWorkflowRuntime registers the ownership services, so DI
        // selects the widest constructor — the one that threads the tracer. Constructed explicitly here so the test
        // doesn't depend on the full ownership wiring while still exercising the tracer-carrying constructor.
        services.AddSingleton(sp => new RuntimeCheckpointCommitter(
            sp.GetRequiredService<IRuntimeCheckpointPersistencePolicy>(),
            sp.GetRequiredService<IRuntimeCheckpointCommitStore>(),
            ownershipContextAccessor: null,
            tracer: sp.GetRequiredService<IWorkflowEngineTracer>()));

        services.AddSingleton<RuntimeActivityLoadStateMiddleware>();
        services.AddSingleton<RuntimeActivityInputEvaluationMiddleware>();
        services.AddSingleton<RuntimeActivityInvokeMiddleware>();
        services.AddSingleton<RuntimeActivityOutputCaptureMiddleware>();
        services.AddSingleton<RuntimeActivitySchedulingMiddleware>();
        services.AddSingleton<RuntimeActivityCheckpointMiddleware>();
        services.AddSingleton<RuntimeActivityPostCommitMiddleware>();

        services.AddSingleton<RuntimeWorkflowLoadStateMiddleware>();
        services.AddSingleton<RuntimeWorkflowInvokeMiddleware>();
        services.AddSingleton<RuntimeWorkflowSchedulingMiddleware>();
        services.AddSingleton<RuntimeWorkflowCheckpointMiddleware>();
        services.AddSingleton<RuntimeWorkflowPostCommitMiddleware>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A context-aware handler that runs in the Invoke slot (producing the activity.execute span) and stages a single
    /// checkpoint commit for the Checkpoint slot to persist (producing the checkpoint.commit span).
    /// </summary>
    private sealed class StagingHandler : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
    {
        public string Name => nameof(StagingHandler);

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
        {
            pipelineContext.Workspace.StageCheckpointCommit(NewCommit(workItem.WorkflowExecutionId));
            return ValueTask.CompletedTask;
        }

        private static RuntimeCheckpointCommit NewCommit(string workflowExecutionId) =>
            new(
                CommitId: "commit-1",
                Checkpoint: new RuntimeCheckpoint(
                    CheckpointId: "cp-1",
                    Name: "test.checkpoint",
                    WorkflowExecutionId: workflowExecutionId,
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
                    operational: []),
                PostCommitIntents: [],
                Metadata: new Dictionary<string, string>());
    }
}
