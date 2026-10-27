using Bpmn.Model.State;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Synthetic delivery regression controls; actual old-state recovery is tested separately against frozen EF stores.</summary>
public sealed class BpmnDeliveryBoundaryTests
{
    [Theory]
    [InlineData("element")]
    [InlineData("duplicate-token")]
    [InlineData("duplicate-handle")]
    [InlineData("superseded-chain")]
    [InlineData("broken-supersession")]
    public async Task Terminal_callback_validates_complete_durable_identity_before_semantic_dispatch(string defect)
    {
        await using var host = await F.CreateAsync(["identity-parent", "identity-child"]);
        var executable = host.NewExecutable([F.EventWaitNode("node-child", "wait")],
            [F.StartEvent(), F.Task("work", "node-child"), F.EndEvent()],
            [F.Flow("start-work", "start", "work"), F.Flow("work-end", "work", "end")],
            [F.NodeResumeTarget("node-child", Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)]);
        var run = await host.RunAsync(executable);
        var parent = run.State(F.ProcessNodeId);
        var child = run.State("node-child") with { Status = ActivityExecutionStatus.Completed };
        var state = new BpmnStateCodec().Read(parent.PrivateState).State;
        ActivityExecutionState[] children = defect switch
        {
            "element" => [child with { Provenance = child.Provenance with
                { Metadata = new Dictionary<string, string>(child.Provenance.Metadata) { ["bpmn.elementId"] = "wrong-element" } } }],
            "duplicate-token" => [child, child with { Execution = child.Execution with { ActivityExecutionId = "another-handle" } }],
            _ => [child, child]
        };
        if (defect is "superseded-chain" or "broken-supersession")
        {
            var previous = child with
            {
                Execution = child.Execution with { ActivityExecutionId = "previous-handle" }, Status = ActivityExecutionStatus.Superseded,
                SupersededByActivityExecutionId = child.InvocationId, SupersededAt = DateTimeOffset.UnixEpoch
            };
            child = child with { Metadata = new Dictionary<string, string>(child.Metadata)
                { [RuntimeMetadataKeys.SupersedesActivityExecutionId] = defect == "superseded-chain" ? previous.InvocationId : "wrong-predecessor" } };
            children = [previous, child];
        }
        if (defect == "superseded-chain")
        {
            Assert.Equal(child, BpmnWorkLedger.Child(children, state, parent.InvocationId, child.InvocationId, child.Execution.ExecutableNodeId, child.IterationId));
            var runtime = host.Provider.GetRequiredService<IBpmnProcessRuntime>();
            var context = new SimpleActivityExecutionContext(new Activities.BpmnProcess(runtime), CancellationToken.None,
                "wfexec-1", WorkflowExecutionHarness.Identity,
                new RuntimeSchedulerWorkItem("history", "wfexec-1", "command", WorkflowExecutionCommandKind.InvokeActivity,
                    "envelope", "idempotency", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), executable.RootActivity, parent,
                liveChildActivities: children.Select(candidate => new RuntimeLiveChildActivity(candidate.InvocationId,
                    candidate.Execution.ExecutableNodeId, candidate.Status, candidate.IterationId)).ToArray());
            Assert.Equal(child.InvocationId, Assert.Single(BpmnWorkLedger.Live(context, state, children)).Handle);
            var tokenId = BpmnWorkLedger.TokenId(child);
            var legacy = new BpmnStateCodec().Read(parent.PrivateState) with { Version = 1, State = state with
            {
                ActiveWork = [], Tokens = state.Tokens.Select(token => token.TokenId == tokenId
                    ? token with { Status = BpmnTokenStatus.Canceled, StoppedHandle = null } : token).ToArray()
            } };
            var recovered = BpmnWorkLedger.RecoverLegacy(legacy, children, parent.InvocationId, BpmnGraphAdapter.Build(executable.RootActivity));
            Assert.Equal(child.InvocationId, recovered.State.Tokens.Single(token => token.TokenId == tokenId).StoppedHandle);
        }
        else
            Assert.Throws<BpmnHostException>(() => BpmnWorkLedger.Child(children, state, parent.InvocationId,
                child.InvocationId, child.Execution.ExecutableNodeId, child.IterationId));
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("terminate")]
    [InlineData("missing")]
    public async Task Notifications_cannot_bypass_an_ended_verdict_or_initialize_a_missing_checkpoint(string prior)
    {
        await using var host = await F.CreateAsync(["delivery-parent", "delivery-child"]);
        var executable = host.NewExecutable([F.EventWaitNode("node-child", "wait")],
            [F.StartEvent(), F.Task("work", "node-child"), F.EndEvent()],
            [F.Flow("start-work", "start", "work"), F.Flow("work-end", "work", "end")],
            [F.NodeResumeTarget("node-child", Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)]);
        var run = await host.RunAsync(executable);
        var parent = run.State(F.ProcessNodeId);
        var child = run.State("node-child");
        var codec = new BpmnStateCodec();
        var source = codec.Read(parent.PrivateState);
        var state = source.State with
        {
            ActiveWork = [], Terminated = prior == "terminate",
            PendingFault = prior == "fault" ? new BpmnPendingFault("original.deferred", "original verdict") : null
        };
        parent = parent with { PrivateState = prior == "missing" ? null : new ActivityPrivateState(parent.InvocationId, 1,
            codec.Write(source with { Version = 1 }, state), "legacy-attempt", DateTimeOffset.UnixEpoch) };
        var runtime = host.Provider.GetRequiredService<IBpmnProcessRuntime>();
        var context = new SimpleActivityExecutionContext(new Activities.BpmnProcess(runtime), CancellationToken.None,
            "wfexec-1", WorkflowExecutionHarness.Identity,
            new RuntimeSchedulerWorkItem("signal", "wfexec-1", "command", WorkflowExecutionCommandKind.InvokeActivity,
                "envelope", "idempotency", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            executable.RootActivity, parent, liveChildActivities:
            [new RuntimeLiveChildActivity(child.InvocationId, child.Execution.ExecutableNodeId, child.Status, child.IterationId)]);
        var notification = new ActivityChildNotifiedContext(child.InvocationId, child.Execution.ExecutableNodeId, "test.notification");

        if (prior == "missing")
            await Assert.ThrowsAsync<BpmnHostException>(async () => await runtime.SignalAsync(context, notification));
        else
        {
            var result = await runtime.SignalAsync(context, notification);
            Assert.Equal(prior == "fault" ? RuntimeStructuralContinuationKind.Fault : RuntimeStructuralContinuationKind.Complete, result.Kind);
            if (prior == "fault")
                Assert.Equal("original.deferred", result.Fault!.Code);
            else
                Assert.Single(context.GetChildSubtreeCancellationRequests());
        }
        Assert.Empty(context.GetChildActivityScheduleRequests());
    }
}
