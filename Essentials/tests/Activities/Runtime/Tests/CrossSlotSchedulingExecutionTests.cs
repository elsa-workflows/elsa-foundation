using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 258 research R5/R6, which a compensation hand-over depends on: once an inner composite completed, an outer
/// structural parent may schedule, as its own child, a node that lives in the inner composite's slot, and that node's
/// ActivityResult reference to the inner composite's forward step resolves through scheduling lineage, also when the
/// inner composite ran as a loop iteration.
/// </summary>
public sealed class CrossSlotSchedulingExecutionTests
{
    [Fact]
    public async Task AnOuterParentSchedulesANodeFromAnInnerSlot_AndItReadsTheInnerForwardResult()
    {
        var log = new CrossSlotLog();
        await using var harness = NewHarness(log);

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-outer", typeof(CrossSlotOuterActivity),
                    StructuralExecutionTestSupport.NewStructuralNode("node-inner", typeof(CrossSlotInnerActivity),
                        Producer("node-forward"),
                        Consumer("node-handler")))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Equal(["forward-result"], log.Values);
        run.AssertWorkflowCompleted();
        Assert.Equal(run.State("node-outer").Execution.ActivityExecutionId, run.State("node-handler").ParentActivityExecutionId);
    }

    [Fact]
    public async Task AForwardResultInsideALoopIterationResolvesForANodeScheduledOutsideTheLoop()
    {
        var log = new CrossSlotLog();
        await using var harness = NewHarness(log);

        var run = await harness.RunAsync(
            StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-outer", typeof(CrossSlotOuterActivity),
                    StructuralExecutionTestSupport.NewStructuralNode("node-loop", typeof(CrossSlotLoopActivity),
                        StructuralExecutionTestSupport.NewStructuralNode("node-inner", typeof(CrossSlotInnerActivity),
                            Producer("node-forward"),
                            Consumer("node-handler"))))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        // Iteration identity sits on the loop body (the inner composite), not on its children, so the forward
        // result is visible to a consumer outside the iteration.
        Assert.Null(run.State("node-forward").Provenance.IterationId);
        Assert.NotNull(run.State("node-inner").Provenance.IterationId);
        Assert.Equal(["forward-result"], log.Values);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(CrossSlotLog log) =>
        WorkflowExecutionHarness.Create()
            .ConfigureServices(services => services.AddSingleton(log))
            .Build("actexec-1", "actexec-2", "actexec-3", "actexec-4", "actexec-5", "actexec-6", "actexec-7", "actexec-8");

    private static ExecutableNode Producer(string nodeId) =>
        StructuralExecutionTestSupport.NewStructuralNode(nodeId, typeof(CrossSlotProducerActivity)).WithoutSlots();

    private static ExecutableNode Consumer(string nodeId)
    {
        var binding = new RuntimeInputBinding(
            nameof(CrossSlotConsumerActivity.Value),
            new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("node-forward", "Value", "authored-node-inner"));
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(CrossSlotConsumerActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/leaf",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "leaf" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding },
            metadata: new Dictionary<string, string>());
    }

    public sealed class CrossSlotLog
    {
        public List<string> Values { get; } = [];
        public string? ForwardActivityExecutionId { get; set; }
    }

    public sealed record CrossSlotResult([property: Output(Key = "Value", Path = "value")] string Value);

    public sealed class CrossSlotProducerActivity : Activity<CrossSlotResult>
    {
        protected override ValueTask<ActivityTransition<CrossSlotResult>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(ActivityTransition.Complete(new CrossSlotResult("forward-result")));
    }

    public sealed class CrossSlotConsumerActivity(CrossSlotLog log) : Activity<ActivityUnit>
    {
        [ActivityInput] public string? Value { get; set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            log.Values.Add(Value ?? "<null>");
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
        }
    }

    /// <summary>Runs only the first slot child (the forward step) and records its execution id.</summary>
    public sealed class CrossSlotInnerActivity(CrossSlotLog log) : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var forward = Assert.Single(context.ExecutableNode.ChildSlots).Activities.First();
            context.ScheduleChildActivity(forward.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            log.ForwardActivityExecutionId = context.CompletedChildActivityExecutionId;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>Runs its single child once as a loop iteration, the way ForEach schedules a body.</summary>
    public sealed class CrossSlotLoopActivity : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var body = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            var parentId = context.ActivityExecutionState.Execution.ActivityExecutionId;
            var iterationId = $"{parentId}:iteration:0";
            context.ScheduleChildActivity(
                body.ExecutableNodeId,
                parentId,
                null,
                ActivitySchedulingProvenance.From(
                    context.WorkflowExecutionId,
                    parentActivityExecutionId: parentId,
                    schedulingActivityExecutionId: parentId,
                    branchId: null,
                    iterationId: iterationId,
                    executionPathId: null,
                    executionScopeId: null,
                    schedulingCause: "spike.iteration"),
                new LoopIterationScopeRequest(context.ExecutableNode.ExecutableNodeId, iterationId, new Dictionary<string, ValueEnvelope>
                {
                    ["CurrentValue"] = ValueEnvelope.Inline(new ValueTypeDescriptor("Int32"), JsonSerializer.SerializeToElement(0), ValueProtectionPolicy.InstanceInline)
                }));
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }

    /// <summary>
    /// Schedules its only child; when it completes, schedules the inner composite's handler node as its own child with
    /// the forward step as scheduling identity, then completes when that handler completes.
    /// </summary>
    public sealed class CrossSlotOuterActivity(CrossSlotLog log) : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            if (context.CompletedChildExecutableNodeId == "node-handler")
                return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

            var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
            runtimeContext.ScheduleChildActivity("node-handler", log.ForwardActivityExecutionId ?? throw new InvalidOperationException("No forward."));
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
    }
}

internal static class CrossSlotNodeExtensions
{
    public static ExecutableNode WithoutSlots(this ExecutableNode node) =>
        new(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptorType: "test/leaf",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "leaf" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
}
