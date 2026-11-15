using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class BpmnCalledWorkflowControlEfTests
{
    private const string EmitWaitNodeId = "node-emit-wait";
    private const string FinishWaitNodeId = "node-finish-wait";

    [Theory]
    [InlineData(false, PersistenceMode.Durable)]
    [InlineData(false, PersistenceMode.Balanced)]
    [InlineData(true, PersistenceMode.Durable)]
    [InlineData(true, PersistenceMode.Balanced)]
    public async Task Concurrent_same_binding_calls_keep_exact_child_control_and_cancellation_isolated_across_restarts(
        bool interrupting, PersistenceMode mode)
    {
        WorkflowExecutableIdentity childPin;
        WorkflowDispatchRecord selected;
        WorkflowDispatchRecord other;
        string selectedToken;
        string otherToken;
        string originalOther;
        string originalSelected;
        await using (var first = await StartAsync(mode, new CalledResumeCut(), "isolation-first"))
        {
            var child = await ConcurrentChildAsync();
            childPin = child.Identity;
            var reference = await first.PublishAsync(child, "source-isolation-child");
            var parent = await first.PublishAsync(await ConcurrentCallerAsync(reference, childPin, interrupting), "source-isolation-caller");
            await first.StartPublishedAsync(parent, ParentExecutionId,
                partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
            await DrainCalledAsync(first);
            var run = await first.ReadRunAsync(ParentExecutionId);
            var calls = run.States(DispatchNodeId);
            Assert.Equal(2, calls.Count);
            Assert.All(calls, call =>
            {
                Assert.Equal(ActivityExecutionStatus.Suspended, call.Status);
                Assert.Null(call.IterationId);
                Assert.Equal("call", call.Provenance.Metadata["bpmn.elementId"]);
                Assert.Equal(run.State(F.ProcessNodeId).InvocationId, call.ParentActivityExecutionId);
            });
            Assert.NotEqual(calls[0].InvocationId, calls[1].InvocationId);
            var semanticCalls = ActiveCallTokens(run);
            Assert.Equal(2, semanticCalls.Count);
            otherToken = semanticCalls[0];
            selectedToken = semanticCalls[1];
            Assert.False(string.IsNullOrWhiteSpace(selectedToken));
            Assert.False(string.IsNullOrWhiteSpace(otherToken));
            Assert.NotEqual(selectedToken, otherToken);
            var selectedCall = Assert.Single(calls, call => call.Provenance.ExecutionPathId == selectedToken);
            var otherCall = Assert.Single(calls, call => call.Provenance.ExecutionPathId == otherToken);
            Assert.NotEqual(semanticCalls[0], selectedCall.Provenance.ExecutionPathId);
            await using var scope = first.Services.CreateAsyncScope();
            var dispatches = await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>().ListAsync(ParentExecutionId);
            Assert.Equal(2, dispatches.Count);
            selected = Assert.Single(dispatches, item => item.ParentActivityExecutionId == selectedCall.InvocationId);
            other = Assert.Single(dispatches, item => item.ParentActivityExecutionId == otherCall.InvocationId);
            Assert.NotEqual(selected.ChildWorkflowExecutionId, other.ChildWorkflowExecutionId);
            Assert.Equal(childPin, selected.ChildExecutable);
            Assert.Equal(childPin, other.ChildExecutable);
            Assert.Equal(2, ActiveCallTokens(run).Count);
            originalOther = await LinkedCallFactsAsync(first, other, childPin, mode, WorkflowExecutionStatus.Running);
            originalSelected = await LinkedCallFactsAsync(first, selected, childPin, mode, WorkflowExecutionStatus.Running);
        }

        var firstDelivery = new CalledResumeCut();
        string stableAfterSelected;
        string finalSelected;
        var finishedChildStatus = interrupting ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Completed;
        await using (var second = await StartAsync(mode, firstDelivery, "isolation-selected"))
        {
            Assert.Equal(originalOther, await LinkedCallFactsAsync(second, other, childPin, mode, WorkflowExecutionStatus.Running));
            Assert.Equal(originalSelected, await LinkedCallFactsAsync(second, selected, childPin, mode, WorkflowExecutionStatus.Running));
            await ResumeCalledWaitAsync(second, selected.ChildWorkflowExecutionId, EmitWaitNodeId, "emit-warning");
            await DrainCalledAsync(second);
            var control = Assert.Single(firstDelivery.ControlCalls);
            Assert.Equal(selected.ParentActivityExecutionId, control.CallingActivityExecutionId);
            Assert.Equal([0L], control.Ordinals);
            var observed = ReadControl(firstDelivery.ObservedIntents.First(intent => intent.Kind == NotifyControlIntentKind));
            Assert.Equal(selected.ChildWorkflowExecutionId, observed.Batch.ChildWorkflowExecutionId);
            Assert.Equal(selected.ParentActivityExecutionId, observed.CallingActivityExecutionId);
            AssertCancellationTarget(firstDelivery, interrupting, selected);
            Assert.Equal(originalOther, await LinkedCallFactsAsync(second, other, childPin, mode, WorkflowExecutionStatus.Running));
            var signalling = await second.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Running, signalling.WorkflowState!.Status);
            Assert.Equal(interrupting ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Suspended,
                Assert.Single(signalling.States(DispatchNodeId), state => state.InvocationId == selected.ParentActivityExecutionId).Status);
            Assert.Equal(ActivityExecutionStatus.Suspended, signalling.State(WarningNodeId).Status);
            await ResumeCalledWaitAsync(second, ParentExecutionId, WarningNodeId, "warning-handler-continue");
            await DrainCalledAsync(second);
            if (!interrupting)
            {
                await ResumeCalledWaitAsync(second, selected.ChildWorkflowExecutionId, FinishWaitNodeId, "finish-child");
                await DrainCalledAsync(second);
                var selectedCalls = firstDelivery.ControlCalls.ToArray();
                Assert.Equal(2, selectedCalls.Length);
                Assert.All(selectedCalls, item => Assert.Equal(selected.ParentActivityExecutionId, item.CallingActivityExecutionId));
                Assert.Equal([1L], selectedCalls[1].Ordinals);
            }
            var afterSelected = await second.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Running, afterSelected.WorkflowState!.Status);
            Assert.Equal(otherToken, Assert.Single(ActiveCallTokens(afterSelected)));
            Assert.DoesNotContain(selectedToken, ActiveCallTokens(afterSelected));
            Assert.Equal(originalOther, await LinkedCallFactsAsync(second, other, childPin, mode, WorkflowExecutionStatus.Running));
            finalSelected = await LinkedCallFactsAsync(second, selected, childPin, mode, finishedChildStatus);
            stableAfterSelected = await EscalationFactsAsync(second, other.ChildWorkflowExecutionId, childPin, mode);
        }

        var otherDelivery = new CalledResumeCut();
        await using (var third = await StartAsync(mode, otherDelivery, "isolation-other"))
        {
            Assert.Equal(stableAfterSelected, await EscalationFactsAsync(third, other.ChildWorkflowExecutionId, childPin, mode));
            Assert.Equal(finalSelected, await LinkedCallFactsAsync(third, selected, childPin, mode, finishedChildStatus));
            await ResumeCalledWaitAsync(third, other.ChildWorkflowExecutionId, EmitWaitNodeId, "emit-warning");
            await DrainCalledAsync(third);
            var control = Assert.Single(otherDelivery.ControlCalls);
            Assert.Equal(other.ParentActivityExecutionId, control.CallingActivityExecutionId);
            Assert.Equal([0L], control.Ordinals);
            AssertCancellationTarget(otherDelivery, interrupting, other);
            Assert.Equal(finalSelected, await LinkedCallFactsAsync(third, selected, childPin, mode, finishedChildStatus));
            var pending = await third.ReadRunAsync(ParentExecutionId);
            Assert.Equal(2, pending.States(WarningNodeId).Count);
            Assert.Single(pending.States(WarningNodeId), state => state.Status == ActivityExecutionStatus.Suspended);
            await ResumeCalledWaitAsync(third, ParentExecutionId, WarningNodeId, "warning-handler-continue");
            await DrainCalledAsync(third);
            if (!interrupting)
            {
                Assert.Equal(WorkflowExecutionStatus.Running, (await third.ReadRunAsync(ParentExecutionId)).WorkflowState!.Status);
                await ResumeCalledWaitAsync(third, other.ChildWorkflowExecutionId, FinishWaitNodeId, "finish-child");
                await DrainCalledAsync(third);
                var otherCalls = otherDelivery.ControlCalls.ToArray();
                Assert.Equal(2, otherCalls.Length);
                Assert.All(otherCalls, item => Assert.Equal(other.ParentActivityExecutionId, item.CallingActivityExecutionId));
                Assert.Equal([1L], otherCalls[1].Ordinals);
            }
            var final = await third.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Completed, final.WorkflowState!.Status);
            Assert.Equal(WorkflowExecutionHarness.Identity, final.WorkflowState.PinnedExecutable);
            Assert.Equal(mode.ToString(), final.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            Assert.Null(final.State(F.ProcessNodeId).PrivateState); // Normal terminal structural-state cleanup.
            Assert.Equal(2, final.States(DispatchNodeId).Count);
            Assert.All(final.States(DispatchNodeId), call => Assert.Equal(interrupting ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Completed, call.Status));
            Assert.Equal(2, final.States(WarningNodeId).Count);
            Assert.All(final.States(WarningNodeId), state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));
            if (interrupting) final.AssertDidNotRun(NormalNodeId);
            else
            {
                Assert.Equal(2, final.States(NormalNodeId).Count);
                Assert.All(final.States(NormalNodeId), state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));
            }
            Assert.Equal(finalSelected, await LinkedCallFactsAsync(third, selected, childPin, mode, finishedChildStatus));
            await LinkedCallFactsAsync(third, other, childPin, mode, finishedChildStatus);
            await using var scope = third.Services.CreateAsyncScope();
            foreach (var id in new[] { ParentExecutionId, selected.ChildWorkflowExecutionId, other.ChildWorkflowExecutionId })
            {
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(id));
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(id));
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(id));
            }
        }
    }

    private static IReadOnlyList<string> ActiveCallTokens(WorkflowExecutionRun run) =>
        run.State(F.ProcessNodeId).PrivateState!.Value.InlineValue!.Value.GetProperty("activeWork").EnumerateArray()
            .Where(item => item.GetProperty("nodeId").GetString() == DispatchNodeId)
            .Select(item => item.GetProperty("tokenId").GetString()!).ToArray();

    private static void AssertCancellationTarget(CalledResumeCut observation, bool interrupting, WorkflowDispatchRecord target)
    {
        var cancellations = observation.ObservedIntents.Where(intent => intent.Kind == DispatchWorkflowConstants.CancelChildIntentKind)
            .GroupBy(intent => intent.IntentId, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        if (!interrupting) Assert.Empty(cancellations);
        else
        {
            var payload = Assert.Single(cancellations).Payload!.Value.Deserialize<WorkflowDispatchChildCancelPayload>(Json)!;
            Assert.Equal(target.DispatchId, payload.DispatchId);
            Assert.Equal(target.ParentActivityExecutionId, payload.ParentActivityExecutionId);
            Assert.Equal(target.ChildWorkflowExecutionId, payload.ChildWorkflowExecutionId);
        }
    }

    private static async Task<string> LinkedCallFactsAsync(WorkflowExecutionHarness host, WorkflowDispatchRecord target,
        WorkflowExecutableIdentity pin, PersistenceMode mode, WorkflowExecutionStatus expectedStatus)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var call = Assert.IsType<ActivityExecutionState>(await provider.GetRequiredService<EfActivityExecutionStateStore>()
            .FindAsync(ParentExecutionId, target.ParentActivityExecutionId));
        var dispatch = Assert.IsType<WorkflowDispatchRecord>(await provider.GetRequiredService<IWorkflowDispatchStore>().FindAsync(target.DispatchId));
        Assert.Equal(target.ChildWorkflowExecutionId, dispatch.ChildWorkflowExecutionId);
        Assert.Equal(target.ParentActivityExecutionId, dispatch.ParentActivityExecutionId);
        Assert.Equal(expectedStatus switch
        {
            WorkflowExecutionStatus.Running => WorkflowDispatchStatus.Started,
            WorkflowExecutionStatus.Completed => WorkflowDispatchStatus.Completed,
            WorkflowExecutionStatus.Cancelled => WorkflowDispatchStatus.Cancelled,
            _ => throw new InvalidOperationException("This isolation fixture has no other child outcome.")
        }, dispatch.Status);
        var workflow = Assert.IsType<WorkflowExecutionState>(await provider.GetRequiredService<EfWorkflowExecutionStateStore>().FindAsync(target.ChildWorkflowExecutionId));
        Assert.Equal(expectedStatus, workflow.Status);
        Assert.Equal(pin, workflow.PinnedExecutable);
        Assert.Equal(mode.ToString(), workflow.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
        var activities = await provider.GetRequiredService<EfActivityExecutionStateStore>().ListAllAsync(target.ChildWorkflowExecutionId);
        var incidents = await provider.GetRequiredService<EfIncidentStateStore>().ListAsync(target.ChildWorkflowExecutionId);
        var bookmarks = provider.GetRequiredService<EfBookmarkStateStore>();
        var physical = new List<BookmarkStateObservation>();
        foreach (var wait in (await bookmarks.ListAllBookmarkStatesAsync(target.ChildWorkflowExecutionId)).OrderBy(wait => wait.BookmarkId, StringComparer.Ordinal))
            physical.Add(Assert.IsType<BookmarkStateObservation>(await bookmarks.ObserveAsync(target.ChildWorkflowExecutionId, wait.BookmarkId)));
        if (expectedStatus == WorkflowExecutionStatus.Running)
            Assert.Equal(EmitWaitNodeId, Assert.Single(physical).State.ExecutableNodeId);
        else Assert.Empty(physical);
        return JsonSerializer.Serialize(new { call, dispatch, workflow.Status, workflow.PinnedExecutable, mode,
            activities = activities.OrderBy(state => state.InvocationId, StringComparer.Ordinal),
            incidents = incidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal), physical }, Json);
    }

    private static async Task<WorkflowExecutable> ConcurrentChildAsync()
    {
        await using var builder = await F.CreateAsync([]);
        var graph = builder.NewExecutable([F.EventWaitNode(EmitWaitNodeId, "emit-warning"), F.EventWaitNode(FinishWaitNodeId, "finish-child")],
            [F.StartEvent(), F.Task("emit-wait", EmitWaitNodeId), F.EscalationThrow("notice", "WARN"), F.Task("finish-wait", FinishWaitNodeId), F.EndEvent()],
            [F.Flow("begin", "start", "emit-wait"), F.Flow("emit", "emit-wait", "notice"), F.Flow("park", "notice", "finish-wait"), F.Flow("finish", "finish-wait", "end")],
            [F.NodeResumeTarget(EmitWaitNodeId, Event.ResumeTargetId), F.NodeResumeTarget(FinishWaitNodeId, Event.ResumeTargetId)]);
        return new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-called-child", "definition-called-child", "version-called-child", "1.0.0", "sha256:called-child"),
            graph.RootActivity, graph.ResumeTargets, graph.CreatedAt, graph.CompatibilityMetadata,
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), [], graph.IncidentStrategy);
    }

    private static async Task<WorkflowExecutable> ConcurrentCallerAsync(WorkflowExecutableSourceReference reference, WorkflowExecutableIdentity pin, bool interrupting)
    {
        var original = await ParentExecutableAsync(reference, pin);
        await using var builder = await F.CreateAsync([]);
        return builder.NewExecutable([original.NodesById[DispatchNodeId], F.EventWaitNode(WarningNodeId, "warning-handler-continue"), builder.NewProbeNode(NormalNodeId)],
            [F.StartEvent(), new BpmnElement("fork", BpmnElementTypes.ParallelGateway), F.CallActivity("call", DispatchNodeId),
                F.EscalationBoundary("warning", "call", "WARN", interrupting), F.Task("handler", WarningNodeId),
                F.Task("normal", NormalNodeId), F.EndEvent("normal-end"), F.EndEvent("warning-end")],
            [F.Flow("begin", "start", "fork"), F.Flow("left-token", "fork", "call"), F.Flow("right-token", "fork", "call"),
                F.Flow("normal", "call", "normal"), F.Flow("finish", "normal", "normal-end"),
                F.Flow("warn", "warning", "handler"), F.Flow("warn-end", "handler", "warning-end")],
            [F.NodeResumeTarget(DispatchNodeId, DispatchWorkflowConstants.CompletionResumeTargetId), F.NodeResumeTarget(WarningNodeId, Event.ResumeTargetId)],
            controlCapable: true);
    }
}
