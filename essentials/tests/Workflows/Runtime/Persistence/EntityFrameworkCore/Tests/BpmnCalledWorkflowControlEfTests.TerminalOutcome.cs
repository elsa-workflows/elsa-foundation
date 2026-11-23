using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
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
    [Theory]
    [InlineData(false, PersistenceMode.Durable)]
    [InlineData(false, PersistenceMode.Balanced)]
    [InlineData(true, PersistenceMode.Durable)]
    [InlineData(true, PersistenceMode.Balanced)]
    public async Task Actual_terminal_notification_and_authored_failure_stay_indivisible_through_recovered_caller(
        bool interrupting, PersistenceMode mode)
    {
        var cut = new CalledResumeCut { RefuseDelivery = true };
        WorkflowExecutableIdentity childPin;
        string childId;
        RuntimeChildControlTransport original;
        string originalIntent;
        string childFacts;
        await using (var first = await StartAsync(mode, cut, "terminal-outcome-first"))
        {
            var child = await TerminalNotificationFaultChildAsync();
            childPin = child.Identity;
            var reference = await first.PublishAsync(child, "source-terminal-outcome-child");
            var caller = await first.PublishAsync(await EscalationParentAsync(reference, childPin, interrupting), "source-terminal-outcome-caller");
            await first.StartPublishedAsync(caller, ParentExecutionId,
                partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
            for (var pass = 0; pass < 8 && cut.LastIntent is null; pass++) await first.SweepAsync();
            var intent = Assert.IsType<RuntimePostCommitIntent>(cut.LastIntent);
            originalIntent = JsonSerializer.Serialize(intent, Json);
            var result = intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!.Result;
            original = Assert.IsType<RuntimeChildControlTransport>(result.Control);
            childId = original.Batch.ChildWorkflowExecutionId;
            Assert.Equal(0, original.Batch.Ordinal);
            Assert.True(original.Batch.IsTerminal);
            Assert.Equal("OTHER", original.Batch.AuthoredFailureCode);
            var notification = Assert.Single(original.Batch.Notifications);
            Assert.Equal("bpmn.escalation", notification.Code);
            Assert.Equal("WARN", notification.Payload!.Value.GetProperty("code").GetString());
            Assert.Equal(WorkflowDispatchStatus.Faulted, result.Status);
            Assert.Equal(DispatchWorkflowDiagnostics.FaultedCode, result.DiagnosticMetadata["code"]);
            Assert.DoesNotContain("OTHER", JsonSerializer.Serialize(result.DiagnosticMetadata, Json));
            var pending = await SourceAsync(first, original.SourceOutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, pending.Status);
            Assert.Equal(originalIntent, JsonSerializer.Serialize(pending.Intent, Json));
            Assert.Empty(cut.ControlCalls);
            childFacts = await ChildFactsAsync(first, childId, "OTHER", childPin, mode);
            var waiting = await first.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionStatus.Running, waiting.WorkflowState!.Status);
            Assert.Equal(ActivityExecutionStatus.Suspended, waiting.State(DispatchNodeId).Status);
            waiting.AssertDidNotRun(WarningNodeId);
            waiting.AssertDidNotRun(NormalNodeId);
        }

        _clock.Advance(TimeSpan.FromMinutes(1));
        var recovered = new CalledResumeCut();
        await using (var second = await StartAsync(mode, recovered, "terminal-outcome-recovered"))
        {
            Assert.Equal(childFacts, await ChildFactsAsync(second, childId, "OTHER", childPin, mode));
            await DrainCalledAsync(second);
            var evaluated = Assert.Single(recovered.ControlCalls);
            Assert.Equal(ParentExecutionId, evaluated.WorkflowExecutionId);
            Assert.Equal(original.CallingActivityExecutionId, evaluated.CallingActivityExecutionId);
            Assert.Equal([0L], evaluated.Ordinals);
            var verdict = Assert.Single(recovered.ControlVerdicts);
            var caller = await second.ReadRunAsync(ParentExecutionId);
            Assert.Equal(ActivityExecutionStatus.Completed, caller.State(DispatchNodeId).Status);
            caller.AssertOutcomes(DispatchNodeId, "Faulted");
            Assert.Null(caller.State(DispatchNodeId).Fault);
            var inbox = Assert.IsType<RuntimeChildControlInbox>(caller.State(DispatchNodeId).ChildControlInbox);
            Assert.True(inbox.IsTerminalConsumed);
            Assert.Equal(1, inbox.NextOrdinal);
            Assert.Empty(inbox.Pending);
            Assert.True(inbox.HasAccepted(original));
            Assert.DoesNotContain(recovered.ObservedIntents, intent => intent.Kind == DispatchWorkflowConstants.CancelChildIntentKind);
            Assert.Empty(verdict.CancelledActivities);
            caller.AssertDidNotRun(NormalNodeId);
            if (interrupting)
            {
                Assert.Equal(WorkflowExecutionStatus.Running, caller.WorkflowState!.Status);
                Assert.Equal(ActivityExecutionStatus.Suspended, caller.State(WarningNodeId).Status);
                Assert.Equal(RuntimeStructuralContinuationKind.Defer, verdict.Kind);
                Assert.Equal([WarningNodeId], verdict.ScheduledNodes);
                await ResumeCalledWaitAsync(second, ParentExecutionId, WarningNodeId, "warning-handler-continue");
                await DrainCalledAsync(second);
                var completed = await second.ReadRunAsync(ParentExecutionId);
                Assert.Equal(WorkflowExecutionStatus.Completed, completed.WorkflowState!.Status);
                completed.AssertCompleted(WarningNodeId);
            }
            else
            {
                Assert.Equal(WorkflowExecutionStatus.Faulted, caller.WorkflowState!.Status);
                Assert.Equal("OTHER", caller.State(F.ProcessNodeId).Fault!.Metadata[ActivityFault.FaultTypeMetadataKey]);
                caller.AssertDidNotRun(WarningNodeId);
                Assert.Equal(RuntimeStructuralContinuationKind.Fault, verdict.Kind);
                Assert.Empty(verdict.ScheduledNodes);
            }
            Assert.Equal(childFacts, await ChildFactsAsync(second, childId, "OTHER", childPin, mode));
            var delivered = await SourceAsync(second, original.SourceOutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
            Assert.Equal(originalIntent, JsonSerializer.Serialize(delivered.Intent, Json));
            var final = await second.ReadRunAsync(ParentExecutionId);
            Assert.Equal(WorkflowExecutionHarness.Identity, final.WorkflowState!.PinnedExecutable);
            Assert.Equal(mode.ToString(), final.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            await using var scope = second.Services.CreateAsyncScope();
            foreach (var id in new[] { ParentExecutionId, childId })
            {
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(id));
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(id));
            }
            if (interrupting) Assert.Empty(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(ParentExecutionId));
        }
    }

    private static async Task<WorkflowExecutable> TerminalNotificationFaultChildAsync()
    {
        await using var builder = await F.CreateAsync([]);
        var graph = builder.NewExecutable([], [F.StartEvent(), new BpmnElement("fork", BpmnElementTypes.ParallelGateway),
                F.EscalationEnd("notice", "WARN"), new BpmnElement("error-end", BpmnElementTypes.EndEvent,
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error,
                        new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "OTHER" })])],
            [F.Flow("begin", "start", "fork"), F.Flow("notice-first", "fork", "notice"), F.Flow("fault-second", "fork", "error-end")]);
        return new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-called-child", "definition-called-child", "version-called-child", "1.0.0", "sha256:called-child"),
            graph.RootActivity, graph.ResumeTargets, graph.CreatedAt, graph.CompatibilityMetadata,
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), [], graph.IncidentStrategy);
    }
}
