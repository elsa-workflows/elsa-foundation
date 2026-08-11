using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Builders;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Middleware;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrail (ADR 0029, Move 1): asserts a registered runtime middleware is ACTUALLY invoked during a real work-item
/// dispatch through the drainer — not merely that the builder registers it — so the pipeline cannot silently re-orphan.
/// Also covers the work-item → pipeline-kind selector.
/// </summary>
public sealed class RuntimeExecutionPipelineDispatchTests : RuntimePipelineTestSupport
{

    [Fact]
    public async Task Drainer_RunsRegisteredActivityMiddlewareAroundHandler_OnRealActivityDispatch()
    {
        await using var provider = BuildProvider(services => services.AddSingleton<RecordingActivityMiddleware>());
        var activityPlan = new ActivityRuntimePipelineBuilder()
            .Use<RecordingActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: -100)
            .BuildPlan();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingHandler();
        var drainer = NewDrainer(queue, handler, BuildDispatcher(provider, WorkflowPlan(), activityPlan));
        await queue.EnqueueAsync(NewWorkItem(WorkflowExecutionCommandKind.InvokeActivity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.Equal(1, provider.GetRequiredService<RecordingActivityMiddleware>().Invocations);
        Assert.Equal(["work-1"], handler.WorkItemIds);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task Drainer_RunsRegisteredWorkflowMiddlewareAroundHandler_OnRealWorkflowDispatch()
    {
        await using var provider = BuildProvider(services => services.AddSingleton<RecordingWorkflowMiddleware>());
        var workflowPlan = new WorkflowRuntimePipelineBuilder()
            .Use<RecordingWorkflowMiddleware>(RuntimeWorkflowPipelineSlots.Scheduling, order: -100)
            .BuildPlan();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingHandler();
        var drainer = NewDrainer(queue, handler, BuildDispatcher(provider, workflowPlan, ActivityPlan()));
        await queue.EnqueueAsync(NewWorkItem(WorkflowExecutionCommandKind.Checkpoint));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.Equal(1, provider.GetRequiredService<RecordingWorkflowMiddleware>().Invocations);
        Assert.Equal(["work-1"], handler.WorkItemIds);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task Drainer_MiddlewareThatSkipsNext_PreventsHandlerFromRunning()
    {
        // Proves the handler is genuinely the pipeline's inner terminal delegate, not dispatched independently.
        await using var provider = BuildProvider(services => services.AddSingleton<ShortCircuitActivityMiddleware>());
        var activityPlan = new ActivityRuntimePipelineBuilder()
            .Use<ShortCircuitActivityMiddleware>(RuntimeActivityPipelineSlots.Invoke, order: -100)
            .BuildPlan();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var handler = new RecordingHandler();
        var drainer = NewDrainer(queue, handler, BuildDispatcher(provider, WorkflowPlan(), activityPlan));
        await queue.EnqueueAsync(NewWorkItem(WorkflowExecutionCommandKind.InvokeActivity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.Equal(1, provider.GetRequiredService<ShortCircuitActivityMiddleware>().Invocations);
        Assert.Empty(handler.WorkItemIds);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(result.Items).Status);
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.Start, RuntimePipelineKind.Workflow)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, RuntimePipelineKind.Workflow)]
    [InlineData(WorkflowExecutionCommandKind.Cancel, RuntimePipelineKind.Workflow)]
    [InlineData(WorkflowExecutionCommandKind.RunSchedulerWork, RuntimePipelineKind.Workflow)]
    [InlineData(WorkflowExecutionCommandKind.GeneratedEvent, RuntimePipelineKind.Workflow)]
    [InlineData(WorkflowExecutionCommandKind.ScheduleActivity, RuntimePipelineKind.Activity)]
    [InlineData(WorkflowExecutionCommandKind.StartActivity, RuntimePipelineKind.Activity)]
    [InlineData(WorkflowExecutionCommandKind.InvokeActivity, RuntimePipelineKind.Activity)]
    [InlineData(WorkflowExecutionCommandKind.ResumeBookmark, RuntimePipelineKind.Activity)]
    [InlineData(WorkflowExecutionCommandKind.CreateBookmark, RuntimePipelineKind.Activity)]
    public void Selector_MapsCommandKindToPipeline(WorkflowExecutionCommandKind commandKind, RuntimePipelineKind expected)
    {
        var selected = new RuntimeSchedulerPipelineSelector().Select(NewWorkItem(commandKind));

        Assert.Equal(expected, selected);
    }

    [Fact]
    public void Selector_RoutesParentCompletionEvaluationToActivity_AndRoutingToWorkflow()
    {
        var selector = new RuntimeSchedulerPipelineSelector();

        var parentCompletion = NewCompleteActivityWorkItem(SchedulerCompletionKind.ParentCompletionEvaluation);
        var routing = NewCompleteActivityWorkItem(SchedulerCompletionKind.ActivityCompleted);

        Assert.Equal(RuntimePipelineKind.Activity, selector.Select(parentCompletion));
        Assert.Equal(RuntimePipelineKind.Workflow, selector.Select(routing));
    }

    [Fact]
    public void Selector_RoutesCompleteActivityToWorkflow_WhenPayloadIsAbsentOrMalformed()
    {
        var selector = new RuntimeSchedulerPipelineSelector();
        using var malformed = JsonDocument.Parse("""{"completionKind":"not-a-kind"}""");

        var noPayload = NewWorkItem(WorkflowExecutionCommandKind.CompleteActivity, payload: null);
        var malformedPayload = NewWorkItem(WorkflowExecutionCommandKind.CompleteActivity, index: 2, payload: malformed.RootElement.Clone());

        Assert.Equal(RuntimePipelineKind.Workflow, selector.Select(noPayload));
        Assert.Equal(RuntimePipelineKind.Workflow, selector.Select(malformedPayload));
    }

    [Fact]
    public void Selector_RoutesInvalidParentCompletionPayloadToWorkflow_MatchingTheHandlerThatClaimsIt()
    {
        // The discriminator reads as parent-completion but the payload fails full validation (missing required fields).
        // WorkflowParentActivityCompletionSchedulerWorkHandler.CanHandle rejects it (deserialize throws → false) and the
        // workflow routing handler claims it, so the selector must agree and route to Workflow — not Activity.
        using var invalid = JsonDocument.Parse($$"""{"CompletionKind":{{(int)SchedulerCompletionKind.ParentCompletionEvaluation}}}""");
        var workItem = NewWorkItem(WorkflowExecutionCommandKind.CompleteActivity, payload: invalid.RootElement.Clone());

        Assert.Equal(RuntimePipelineKind.Workflow, new RuntimeSchedulerPipelineSelector().Select(workItem));
    }

    private WorkflowSchedulerDrainer NewDrainer(
        InMemoryWorkflowSchedulerWorkQueue queue,
        IWorkflowSchedulerWorkHandler handler,
        IRuntimeExecutionPipelineDispatcher dispatcher) =>
        TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(Now),
            pauseGate: null,
            workflowExecutionStateStore: null,
            dispatcher);

    private static RuntimeExecutionPipelineDispatcher BuildDispatcher(
        IServiceProvider provider,
        RuntimePipelinePlan workflowPlan,
        RuntimePipelinePlan activityPlan) =>
        new(
            new RuntimeSchedulerPipelineSelector(),
            new RuntimeWorkflowExecutionPipeline(workflowPlan, provider),
            new RuntimeActivityExecutionPipeline(activityPlan, provider));

    private static RuntimePipelinePlan WorkflowPlan() => new WorkflowRuntimePipelineBuilder().BuildPlan();

    private static RuntimePipelinePlan ActivityPlan() => new ActivityRuntimePipelineBuilder().BuildPlan();

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        // The real Checkpoint slot middleware depends on the committer (Move 2); register it so the pipeline resolves.
        services.AddSingleton<InMemoryRuntimeCheckpointCommitStore>();
        services.AddSingleton<IRuntimeCheckpointCommitStore>(sp => sp.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>());
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, ImmediateRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeExecutionOwnershipContextAccessor, AsyncLocalRuntimeExecutionOwnershipContextAccessor>();
        services.AddSingleton<RuntimeCheckpointCommitter>();
        services.AddSingleton<RuntimeWorkflowLoadStateMiddleware>();
        services.AddSingleton<RuntimeWorkflowInvokeMiddleware>();
        services.AddSingleton<RuntimeWorkflowSchedulingMiddleware>();
        services.AddSingleton<RuntimeWorkflowCheckpointMiddleware>();
        services.AddSingleton<RuntimeWorkflowPostCommitMiddleware>();
        services.AddSingleton<RuntimeActivityLoadStateMiddleware>();
        services.AddSingleton<RuntimeActivityInputEvaluationMiddleware>();
        services.AddSingleton<RuntimeActivityInvokeMiddleware>();
        services.AddSingleton<RuntimeActivityOutputCaptureMiddleware>();
        services.AddSingleton<RuntimeActivitySchedulingMiddleware>();
        services.AddSingleton<RuntimeActivityCheckpointMiddleware>();
        services.AddSingleton<RuntimeActivityPostCommitMiddleware>();
        configure(services);

        return services.BuildServiceProvider();
    }

    private RuntimeSchedulerWorkItem NewCompleteActivityWorkItem(SchedulerCompletionKind completionKind)
    {
        var payload = new RuntimeCompleteActivityCommandPayload(
            pinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            executableNodeId: "node-start",
            activityExecutionId: "actexec-parent",
            parentActivityExecutionId: null,
            branchId: null,
            outcomeNames: ["Done"],
            reason: completionKind == SchedulerCompletionKind.ParentCompletionEvaluation
                ? RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason
                : RuntimeCompleteActivityCommandPayload.ActivityInvocationCompletedReason,
            completionKind: completionKind,
            completedChildActivityExecutionId: completionKind == SchedulerCompletionKind.ParentCompletionEvaluation
                ? "actexec-child"
                : null);

        return NewWorkItem(WorkflowExecutionCommandKind.CompleteActivity, payload: JsonSerializer.SerializeToElement(payload));
    }

    private sealed class ShortCircuitActivityMiddleware : ActivityRuntimeMiddlewareBase
    {
        public int Invocations { get; private set; }

        public override ValueTask InvokeAsync(ActivityRuntimePipelineContext context, ActivityRuntimeMiddlewareDelegate next)
        {
            Invocations++;
            return ValueTask.CompletedTask; // Deliberately does not call next.
        }
    }

    private sealed class RecordingWorkflowMiddleware : WorkflowRuntimeMiddlewareBase
    {
        public int Invocations { get; private set; }

        public override ValueTask InvokeAsync(WorkflowRuntimePipelineContext context, WorkflowRuntimeMiddlewareDelegate next)
        {
            Invocations++;
            return next(context);
        }
    }
}
