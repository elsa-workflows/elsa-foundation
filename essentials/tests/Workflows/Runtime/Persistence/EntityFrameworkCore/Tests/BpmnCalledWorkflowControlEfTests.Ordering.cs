using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
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
    private const string OrdinaryFaultNodeId = "node-ordinary-fault";

    [Theory]
    [InlineData(false, PersistenceMode.Durable)]
    [InlineData(false, PersistenceMode.Balanced)]
    [InlineData(true, PersistenceMode.Durable)]
    [InlineData(true, PersistenceMode.Balanced)]
    public async Task Empty_terminal_barrier_cannot_overtake_earlier_escalation_across_provider_restart(
        bool ordinaryFault, PersistenceMode mode)
    {
        var delayed = new CalledResumeCut { HoldNotifications = true };
        WorkflowExecutableIdentity childPin;
        string childId;
        RuntimeChildControlTransport earlier;
        RuntimeChildControlTransport terminal;
        string earlierIntent;
        string terminalSource;
        string completionReceipt;
        string bufferedFacts;
        string terminalChildFacts;
        await using (var first = await StartAsync(mode, delayed, "gap-first"))
        {
            var child = await GapChildAsync(ordinaryFault);
            childPin = child.Identity;
            var reference = await first.PublishAsync(child, "source-gap-child");
            var parent = await first.PublishAsync(await EscalationParentAsync(reference, childPin, false), "source-gap-parent");
            await first.StartPublishedAsync(parent, ParentExecutionId,
                partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
            await DrainCalledAsync(first);
            earlier = ReadControl(Assert.IsType<RuntimePostCommitIntent>(delayed.LastHeldNotification));
            childId = earlier.Batch.ChildWorkflowExecutionId;
            earlierIntent = JsonSerializer.Serialize(delayed.LastHeldNotification, Json);
            Assert.Equal(0, earlier.Batch.Ordinal);
            Assert.False(earlier.Batch.IsTerminal);
            Assert.Equal("bpmn.escalation", Assert.Single(earlier.Batch.Notifications).Code);
            Assert.Null(earlier.Batch.AuthoredFailureCode);
            Assert.Empty(delayed.ControlCalls);
            var waitingParent = await first.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Running, waitingParent.WorkflowState!.Status);
            Assert.Equal(ActivityExecutionStatus.Suspended, waitingParent.State(DispatchNodeId).Status);
            waitingParent.AssertDidNotRun(WarningNodeId);
            waitingParent.AssertDidNotRun(NormalNodeId);
            var originalSemanticState = JsonSerializer.Serialize(waitingParent.State(F.ProcessNodeId).PrivateState, Json);
            await ResumeCalledWaitAsync(first, childId, ChildWaitNodeId, "called-continue");
            await DrainCalledAsync(first);

            var terminalIntent = delayed.ObservedIntents.First(intent => intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind);
            var result = terminalIntent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!.Result;
            terminal = Assert.IsType<RuntimeChildControlTransport>(result.Control);
            Assert.Equal(1, terminal.Batch.Ordinal);
            Assert.True(terminal.Batch.IsTerminal);
            Assert.Empty(terminal.Batch.Notifications);
            Assert.Null(terminal.Batch.AuthoredFailureCode);
            Assert.Equal(earlier.DispatchId, terminal.DispatchId);
            Assert.Equal(earlier.Batch.RootActivityExecutionId, terminal.Batch.RootActivityExecutionId);
            Assert.Equal(ordinaryFault ? WorkflowDispatchStatus.Faulted : WorkflowDispatchStatus.Completed, result.Status);
            if (ordinaryFault) Assert.Equal(DispatchWorkflowDiagnostics.FaultedCode, result.DiagnosticMetadata["code"]);
            else Assert.Empty(result.DiagnosticMetadata);
            var deliveredTerminal = await SourceAsync(first, terminal.SourceOutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, deliveredTerminal.Status);
            terminalSource = JsonSerializer.Serialize(deliveredTerminal, Json);
            var pendingEarlier = await SourceAsync(first, earlier.SourceOutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, pendingEarlier.Status);
            Assert.True(pendingEarlier.RetryPolicy.RetryUntilAcknowledged);
            Assert.True(pendingEarlier.DeliveryAttemptCount > 0);
            Assert.Equal(earlierIntent, JsonSerializer.Serialize(pendingEarlier.Intent, Json));
            Assert.Empty(delayed.ControlCalls);
            var buffered = await first.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Running, buffered.WorkflowState!.Status);
            Assert.Equal(ActivityExecutionStatus.Completed, buffered.State(DispatchNodeId).Status);
            Assert.Equal(originalSemanticState, JsonSerializer.Serialize(buffered.State(F.ProcessNodeId).PrivateState, Json));
            buffered.AssertDidNotRun(WarningNodeId);
            buffered.AssertDidNotRun(NormalNodeId);
            AssertBufferedTerminal(buffered, terminal);
            completionReceipt = buffered.State(DispatchNodeId).Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId];
            Assert.False(string.IsNullOrWhiteSpace(completionReceipt));
            bufferedFacts = await EscalationFactsAsync(first, childId, childPin, mode);
            terminalChildFacts = await GapChildFactsAsync(first, childId, childPin, mode, ordinaryFault);
        }

        var recovered = new CalledResumeCut { HoldNotifications = true };
        await using (var second = await StartAsync(mode, recovered, "gap-recovered"))
        {
            Assert.Equal(bufferedFacts, await EscalationFactsAsync(second, childId, childPin, mode));
            var beforeRetry = (await SourceAsync(second, earlier.SourceOutboxItemId)).DeliveryAttemptCount;
            await DrainCalledAsync(second);
            Assert.True((await SourceAsync(second, earlier.SourceOutboxItemId)).DeliveryAttemptCount > beforeRetry);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, (await SourceAsync(second, earlier.SourceOutboxItemId)).Status);
            Assert.Equal(earlierIntent, JsonSerializer.Serialize((await SourceAsync(second, earlier.SourceOutboxItemId)).Intent, Json));
            Assert.Equal(terminalSource, JsonSerializer.Serialize(await SourceAsync(second, terminal.SourceOutboxItemId), Json));
            Assert.Equal(bufferedFacts, await EscalationFactsAsync(second, childId, childPin, mode));
            AssertBufferedTerminal(await second.ReadRunAsync(ParentExecutionId), terminal);
            Assert.Empty(recovered.ControlCalls);
            Assert.Equal(terminalChildFacts, await GapChildFactsAsync(second, childId, childPin, mode, ordinaryFault));

            recovered.HoldNotifications = false;
            await DrainCalledAsync(second);
            var folded = Assert.Single(recovered.ControlCalls);
            Assert.Equal(ParentExecutionId, folded.WorkflowExecutionId);
            Assert.Equal(earlier.CallingActivityExecutionId, folded.CallingActivityExecutionId);
            Assert.Equal([0L, 1L], folded.Ordinals);
            var caller = await second.ReadRunAsync(ParentExecutionId);
            var inbox = Assert.IsType<RuntimeChildControlInbox>(caller.State(DispatchNodeId).ChildControlInbox);
            Assert.True(inbox.IsTerminalConsumed);
            Assert.Equal(2, inbox.NextOrdinal);
            Assert.Empty(inbox.Pending);
            Assert.True(inbox.HasAccepted(earlier));
            Assert.True(inbox.HasAccepted(terminal));
            Assert.Equal(completionReceipt, caller.State(DispatchNodeId).Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId]);
            caller.AssertOutcomes(DispatchNodeId, ordinaryFault ? "Faulted" : "Completed");
            Assert.Null(caller.State(DispatchNodeId).Fault);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await SourceAsync(second, earlier.SourceOutboxItemId)).Status);
            Assert.Equal(earlierIntent, JsonSerializer.Serialize((await SourceAsync(second, earlier.SourceOutboxItemId)).Intent, Json));
            Assert.Equal(terminalSource, JsonSerializer.Serialize(await SourceAsync(second, terminal.SourceOutboxItemId), Json));
            Assert.Equal(terminalChildFacts, await GapChildFactsAsync(second, childId, childPin, mode, ordinaryFault));
            var verdict = Assert.Single(recovered.ControlVerdicts);
            if (ordinaryFault)
            {
                Assert.Equal(WorkflowExecutionStatus.Faulted, caller.WorkflowState!.Status);
                Assert.Equal("bpmn.call-activity.faulted", caller.State(F.ProcessNodeId).Fault!.Code);
                Assert.Null(caller.State(F.ProcessNodeId).Fault!.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey));
                caller.AssertDidNotRun(WarningNodeId);
                caller.AssertDidNotRun(NormalNodeId);
                Assert.Equal(RuntimeStructuralContinuationKind.Fault, verdict.Kind);
                Assert.Empty(verdict.ScheduledNodes);
            }
            else
            {
                Assert.Equal(WorkflowExecutionStatus.Running, caller.WorkflowState!.Status);
                Assert.Equal(ActivityExecutionStatus.Suspended, caller.State(WarningNodeId).Status);
                caller.AssertCompleted(NormalNodeId);
                Assert.Equal(RuntimeStructuralContinuationKind.Defer, verdict.Kind);
                Assert.Equal(new[] { NormalNodeId, WarningNodeId }.Order(), verdict.ScheduledNodes.Order());
                await ResumeCalledWaitAsync(second, ParentExecutionId, WarningNodeId, "warning-handler-continue");
                await DrainCalledAsync(second);
                Assert.Equal(WorkflowExecutionStatus.Completed, (await second.ReadRunAsync(ParentExecutionId)).WorkflowState!.Status);
            }
            var final = await second.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionHarness.Identity, final.WorkflowState!.PinnedExecutable);
            Assert.Equal(mode.ToString(), final.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            Assert.Equal(terminalChildFacts, await GapChildFactsAsync(second, childId, childPin, mode, ordinaryFault));
            await using var scope = second.Services.CreateAsyncScope();
            foreach (var id in new[] { ParentExecutionId, childId })
            {
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(id));
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(id));
            }
        }
    }

    private static void AssertBufferedTerminal(WorkflowExecutionRun caller, RuntimeChildControlTransport terminal)
    {
        var inbox = Assert.IsType<RuntimeChildControlInbox>(caller.State(DispatchNodeId).ChildControlInbox);
        Assert.False(inbox.IsTerminalConsumed);
        Assert.Equal(0, inbox.NextOrdinal);
        Assert.True(inbox.HasAccepted(terminal));
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(terminal), JsonSerializer.SerializeToElement(Assert.Single(inbox.Pending))));
        caller.AssertDidNotRun(WarningNodeId);
        caller.AssertDidNotRun(NormalNodeId);
    }

    private static async Task<string> GapChildFactsAsync(WorkflowExecutionHarness host, string id, WorkflowExecutableIdentity pin, PersistenceMode mode, bool fault)
    {
        var run = await host.ReadRunAsync(id);
        Assert.Equal(fault ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed, run.WorkflowState!.Status);
        Assert.Equal(pin, run.WorkflowState.PinnedExecutable);
        Assert.Equal(mode.ToString(), run.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
        Assert.Equal(fault ? ActivityExecutionStatus.Faulted : ActivityExecutionStatus.Completed, run.State(F.ProcessNodeId).Status);
        await using var scope = host.Services.CreateAsyncScope();
        var incidents = await scope.ServiceProvider.GetRequiredService<EfIncidentStateStore>().ListAsync(id);
        if (fault)
        {
            Assert.Equal(ActivityExecutionStatus.Faulted, run.State(OrdinaryFaultNodeId).Status);
            Assert.Equal("bpmn.work.faulted", run.State(F.ProcessNodeId).Fault!.Code);
            Assert.Null(run.State(F.ProcessNodeId).Fault!.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey));
            Assert.NotEmpty(incidents);
            Assert.All(incidents, incident => Assert.True(incident.IsBlocking));
        }
        else Assert.Empty(incidents);
        return JsonSerializer.Serialize(new { activities = run.ActivityStates.OrderBy(state => state.InvocationId, StringComparer.Ordinal),
            incidents = incidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal) }, Json);
    }

    private static async Task<WorkflowExecutable> GapChildAsync(bool ordinaryFault)
    {
        await using var builder = await F.CreateAsync([]);
        var elements = new List<Elsa.Activities.Bpmn.Models.BpmnElement>
            { F.StartEvent(), F.EscalationThrow("notice", "WARN"), F.Task("wait", ChildWaitNodeId), F.EndEvent() };
        var flows = new List<Elsa.Activities.Bpmn.Models.BpmnSequenceFlow>
            { F.Flow("emit", "start", "notice"), F.Flow("park", "notice", "wait") };
        var nodes = new List<ExecutableNode> { F.EventWaitNode(ChildWaitNodeId, "called-continue") };
        if (ordinaryFault)
        {
            nodes.Add(builder.NewFaultingNode(OrdinaryFaultNodeId));
            elements.Add(F.Task("fault", OrdinaryFaultNodeId));
            flows.Add(F.Flow("fail", "wait", "fault"));
            flows.Add(F.Flow("finish", "fault", "end"));
        }
        else flows.Add(F.Flow("finish", "wait", "end"));
        var graph = builder.NewExecutable(nodes, elements, flows, [F.NodeResumeTarget(ChildWaitNodeId, Event.ResumeTargetId)]);
        return new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-called-child", "definition-called-child", "version-called-child", "1.0.0", "sha256:called-child"),
            graph.RootActivity, graph.ResumeTargets, graph.CreatedAt, graph.CompatibilityMetadata,
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), [], graph.IncidentStrategy);
    }
}
