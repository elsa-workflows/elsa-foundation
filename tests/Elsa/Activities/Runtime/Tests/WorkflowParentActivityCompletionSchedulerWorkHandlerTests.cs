using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class WorkflowParentActivityCompletionSchedulerWorkHandlerTests
{
    [Fact]
    public async Task ChildCompletionCallback_ReturningUndeclaredOutcome_FaultsParentAndEndsAttempt()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .Build("actexec-parent", "actexec-child");

        var run = await harness.RunAsync(NewExecutable());

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.NotEmpty(parent.IncidentIds);
        Assert.Contains("VF-ACT-006", parent.Fault!.Message, StringComparison.Ordinal);
        Assert.Collection(
            parent.Attempts!.OrderBy(attempt => attempt.Ordinal),
            initialAttempt =>
            {
                Assert.Equal(ActivityAttemptReason.Initial, initialAttempt.Reason);
                Assert.NotNull(initialAttempt.EndedAt);
                Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend, initialAttempt.TransitionKind);
            },
            callbackAttempt =>
            {
                Assert.Equal(ActivityAttemptReason.Resume, callbackAttempt.Reason);
                Assert.NotNull(callbackAttempt.EndedAt);
                Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Fault, callbackAttempt.TransitionKind);
                Assert.Equal(Assert.Single(parent.IncidentIds), callbackAttempt.IncidentId);
            });
    }

    [Fact]
    public async Task ChildCompletionCallback_CompletingParent_DiscardsPrivateState()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .Build("actexec-parent", "actexec-child");

        var run = await harness.RunAsync(NewExecutable(typeof(StateUpdatingStructuralActivity)));

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Completed, parent.Status);
        Assert.Null(parent.PrivateState);
    }

    [Fact]
    public async Task ChildCompletionWithoutCallback_DisposalFailureFaultsBeforeDeferredCheckpoint()
    {
        var probe = new CallbackDisposalProbe();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .ConfigureServices(services => services.AddSingleton(probe))
            .Build("actexec-parent", "actexec-child");

        var run = await harness.RunAsync(NewExecutable(typeof(DisposalFailingNonCallbackStructuralActivity)));

        var parent = run.State("node-parent");
        var child = run.State("node-child");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Equal("ActivityDisposalFailed", parent.SubStatus);
        Assert.Contains("callback disposal failed", parent.Fault!.Message, StringComparison.Ordinal);
        Assert.False(child.Metadata.ContainsKey(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
        Assert.Equal(2, probe.Activations);
        Assert.Equal(2, probe.Disposals);
    }

    [Fact]
    public async Task ChildCompletionCallbackAndDisposalFailures_AreAggregatedIntoOneFault()
    {
        var probe = new CallbackDisposalProbe();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .ConfigureServices(services => services.AddSingleton(probe))
            .Build("actexec-parent", "actexec-child");

        var run = await harness.RunAsync(NewExecutable(typeof(CallbackAndDisposalFailingStructuralActivity)));

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Equal("ActivityDisposalFailed", parent.SubStatus);
        Assert.Contains("Activity execution and activation disposal both failed", parent.Fault!.Message, StringComparison.Ordinal);
        Assert.Single(parent.IncidentIds);
        Assert.Equal(2, probe.Activations);
        Assert.Equal(2, probe.Disposals);
    }

    private static WorkflowExecutable NewExecutable() =>
        NewExecutable(typeof(UndeclaredOutcomeStructuralActivity));

    private static WorkflowExecutable NewExecutable(Type activityType)
    {
        var child = WorkflowExecutionHarness.NewProbeNode("node-child");
        var root = new ExecutableNode(
            executableNodeId: "node-parent",
            authoredActivityId: "authored-parent",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/structural",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "structural" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(UndeclaredOutcomeStructuralActivity.ChildSlotName, [child])]);

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    public sealed class UndeclaredOutcomeStructuralActivity : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler
    {
        public const string ChildSlotName = "Test.Child";

        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete("Undeclared"));
    }

    public sealed class StateUpdatingStructuralActivity : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer.WithState(StateValue(1)));
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete().WithState(StateValue(2)));

        private static ValueEnvelope StateValue(int step) =>
            ValueEnvelope.Inline(
                new ValueTypeDescriptor("test/structural-state"),
                JsonSerializer.SerializeToElement(new { step }),
                ValueProtectionPolicy.InstanceInline);
    }

    public sealed class DisposalFailingNonCallbackStructuralActivity(CallbackDisposalProbe probe) :
        CallbackDisposalFailingStructuralActivity(probe);

    public sealed class CallbackAndDisposalFailingStructuralActivity(CallbackDisposalProbe probe) :
        CallbackDisposalFailingStructuralActivity(probe),
        IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            throw new InvalidOperationException("callback execution failed");
    }

    public abstract class CallbackDisposalFailingStructuralActivity : StructuralActivity,
        IRuntimeStructuralActivity,
        IAsyncDisposable
    {
        private readonly CallbackDisposalProbe _probe;
        private readonly int _activationOrdinal;

        protected CallbackDisposalFailingStructuralActivity(CallbackDisposalProbe probe)
        {
            _probe = probe;
            _activationOrdinal = probe.RecordActivation();
        }

        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask DisposeAsync()
        {
            _probe.RecordDisposal();
            if (_activationOrdinal == 2)
                throw new InvalidOperationException("callback disposal failed");
            return ValueTask.CompletedTask;
        }
    }

    public sealed class CallbackDisposalProbe
    {
        public int Activations { get; private set; }
        public int Disposals { get; private set; }

        public int RecordActivation() => ++Activations;
        public void RecordDisposal() => ++Disposals;
    }
}
