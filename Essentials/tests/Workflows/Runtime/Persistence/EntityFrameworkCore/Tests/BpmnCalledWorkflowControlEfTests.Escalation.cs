using System.Text.Json;
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
    private const string NotifyControlIntentKind = "Elsa.Activities.DispatchWorkflow.NotifyControl";
    private const string WarningNodeId = "node-warning";
    private const string NormalNodeId = "node-normal";
    private const string ChildWaitNodeId = "node-child-wait";

    [Theory]
    [InlineData(false, false, PersistenceMode.Durable)]
    [InlineData(false, false, PersistenceMode.Balanced)]
    [InlineData(true, false, PersistenceMode.Durable)]
    [InlineData(true, false, PersistenceMode.Balanced)]
    [InlineData(false, true, PersistenceMode.Durable)]
    [InlineData(false, true, PersistenceMode.Balanced)]
    [InlineData(true, true, PersistenceMode.Durable)]
    [InlineData(true, true, PersistenceMode.Balanced)]
    public async Task Actual_called_escalation_preserves_running_and_terminal_status_and_exact_live_wait_across_restart(
        bool interrupting, bool terminalEmission, PersistenceMode mode)
    {
        var observed = new CalledResumeCut();
        string childId;
        WorkflowExecutableIdentity childPin;
        RuntimePostCommitIntent originalIntent;
        RuntimeChildControlTransport originalControl;
        string originalSource;
        string parkedFacts;
        await using (var first = await StartAsync(mode, observed, "escalation-first"))
        {
            var child = await EscalatingChildAsync(terminalEmission);
            childPin = child.Identity;
            var childReference = await first.PublishAsync(child, "source-escalating-child");
            var parentReference = await first.PublishAsync(await EscalationParentAsync(childReference, childPin, interrupting), "source-escalation-caller");
            await first.StartPublishedAsync(parentReference, ParentExecutionId,
                partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
            await DrainCalledAsync(first);
            await using var scope = first.Services.CreateAsyncScope();
            var dispatch = Assert.Single(await scope.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>().ListAsync(ParentExecutionId));
            childId = dispatch.ChildWorkflowExecutionId;
            var delivered = observed.ObservedIntents.Where(intent => intent.Kind == (terminalEmission
                    ? DispatchWorkflowConstants.ResumeParentIntentKind : NotifyControlIntentKind))
                .Select(intent => (intent, control: ReadControl(intent)))
                .First(item => item.control.Batch.Ordinal == 0);
            originalIntent = delivered.intent;
            originalControl = delivered.control;
            var evaluated = Assert.Single(observed.ControlCalls);
            Assert.Equal(ParentExecutionId, evaluated.WorkflowExecutionId);
            Assert.Equal(dispatch.ParentActivityExecutionId, evaluated.CallingActivityExecutionId);
            Assert.Equal([0L], evaluated.Ordinals);
            Assert.Equal(childId, originalControl.Batch.ChildWorkflowExecutionId);
            Assert.Equal(dispatch.ParentActivityExecutionId, originalControl.CallingActivityExecutionId);
            Assert.Equal(terminalEmission, originalControl.Batch.IsTerminal);
            Assert.Null(originalControl.Batch.AuthoredFailureCode);
            var notice = Assert.Single(originalControl.Batch.Notifications);
            Assert.Equal("bpmn.escalation", notice.Code);
            Assert.Equal("WARN", notice.Payload!.Value.GetProperty("code").GetString());
            var source = await SourceAsync(first, originalControl.SourceOutboxItemId);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, source.Status);
            Assert.Equal(JsonSerializer.Serialize(originalIntent, Json), JsonSerializer.Serialize(source.Intent, Json));
            originalSource = JsonSerializer.Serialize(source, Json);
            var cancellations = observed.ObservedIntents.Where(intent => intent.Kind == DispatchWorkflowConstants.CancelChildIntentKind)
                .GroupBy(intent => intent.IntentId, StringComparer.Ordinal).Select(group => group.First()).ToArray();
            if (interrupting && !terminalEmission)
            {
                var cancellation = Assert.Single(cancellations).Payload!.Value.Deserialize<WorkflowDispatchChildCancelPayload>(Json)!;
                Assert.Equal(dispatch.DispatchId, cancellation.DispatchId);
                Assert.Equal(childId, cancellation.ChildWorkflowExecutionId);
                Assert.Equal(dispatch.ParentActivityExecutionId, cancellation.ParentActivityExecutionId);
            }
            else Assert.Empty(cancellations);
            await AssertEscalationStageAsync(first, childId, childPin, mode, interrupting, terminalEmission);
            parkedFacts = await EscalationFactsAsync(first, childId, childPin, mode);
        }

        var resumed = new CalledResumeCut();
        await using (var second = await StartAsync(mode, resumed, "escalation-recovered"))
        {
            Assert.Equal(parkedFacts, await EscalationFactsAsync(second, childId, childPin, mode));
            Assert.Equal(originalSource, JsonSerializer.Serialize(await SourceAsync(second, originalControl.SourceOutboxItemId), Json));
            // Repeat the exact originally dispatched input. This is handler duplicate delivery using
            // retained real source authority, not a fabricated outbox claim or a second source retry.
            await using (var scope = second.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>().DispatchAsync(originalIntent);
            await DrainCalledAsync(second);
            Assert.Equal(parkedFacts, await EscalationFactsAsync(second, childId, childPin, mode));
            Assert.Equal(originalSource, JsonSerializer.Serialize(await SourceAsync(second, originalControl.SourceOutboxItemId), Json));
            await AssertEscalationStageAsync(second, childId, childPin, mode, interrupting, terminalEmission);
            Assert.Empty(resumed.ControlCalls);

            // Keep the real warning handler waiting until the child cancellation/status has been
            // checked. Otherwise terminal workflow cleanup could conceal a missing adapter cancel.
            await ResumeCalledWaitAsync(second, ParentExecutionId, WarningNodeId, "warning-handler-continue");
            await DrainCalledAsync(second);
            var afterWarning = await second.ReadRunAsync(ParentExecutionId);
            afterWarning.AssertCompleted(WarningNodeId);
            Assert.Single(afterWarning.States(WarningNodeId));
            Assert.Equal(!interrupting && !terminalEmission ? WorkflowExecutionStatus.Running : WorkflowExecutionStatus.Completed,
                afterWarning.WorkflowState!.Status);

            if (!interrupting && !terminalEmission)
            {
                Assert.Empty(resumed.ControlCalls);
                await using var scope = second.Services.CreateAsyncScope();
                var wait = Assert.Single(await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(childId));
                Assert.Equal(ChildWaitNodeId, wait.ExecutableNodeId);
                await ResumeCalledWaitAsync(second, childId, ChildWaitNodeId, "called-continue");
                await DrainCalledAsync(second);
                var parent = await second.ReadRunAsync(ParentExecutionId);
                Assert.Equal(WorkflowExecutionStatus.Completed, parent.WorkflowState!.Status);
                Assert.Single(parent.States(WarningNodeId));
                parent.AssertCompleted(NormalNodeId);
                parent.AssertOutcomes(DispatchNodeId, "Completed");
                var inbox = Assert.IsType<RuntimeChildControlInbox>(parent.State(DispatchNodeId).ChildControlInbox);
                Assert.True(inbox.IsTerminalConsumed);
                Assert.Equal(2, inbox.NextOrdinal);
                Assert.Empty(inbox.Pending);
                var completedChild = (await second.ReadRunAsync(childId)).WorkflowState!;
                Assert.Equal(WorkflowExecutionStatus.Completed, completedChild.Status);
                Assert.Equal(childPin, completedChild.PinnedExecutable);
                Assert.Equal(mode.ToString(), completedChild.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
                Assert.Equal(WorkflowExecutionHarness.Identity, parent.WorkflowState.PinnedExecutable);
                Assert.Equal(mode.ToString(), parent.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
                var terminal = resumed.ObservedIntents.Where(intent => intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind)
                    .Select(ReadControl).First(control => control.Batch.Ordinal == 1);
                var evaluatedTerminal = Assert.Single(resumed.ControlCalls);
                Assert.Equal(ParentExecutionId, evaluatedTerminal.WorkflowExecutionId);
                Assert.Equal(originalControl.CallingActivityExecutionId, evaluatedTerminal.CallingActivityExecutionId);
                Assert.Equal([1L], evaluatedTerminal.Ordinals);
                Assert.True(terminal.Batch.IsTerminal);
                Assert.Empty(terminal.Batch.Notifications);
                Assert.Null(terminal.Batch.AuthoredFailureCode);
                Assert.Equal(originalControl.Batch.RootActivityExecutionId, terminal.Batch.RootActivityExecutionId);
                Assert.Equal(originalControl.DispatchId, terminal.DispatchId);
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await SourceAsync(second, terminal.SourceOutboxItemId)).Status);
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(childId));
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(ParentExecutionId));
            }

            var finalParent = await second.ReadRunAsync(ParentExecutionId);
            var finalChild = await second.ReadRunAsync(childId);
            Assert.Equal(WorkflowExecutionStatus.Completed, finalParent.WorkflowState!.Status);
            Assert.Equal(terminalEmission || !interrupting ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Cancelled, finalChild.WorkflowState!.Status);
            Assert.Equal(WorkflowExecutionHarness.Identity, finalParent.WorkflowState.PinnedExecutable);
            Assert.Equal(childPin, finalChild.WorkflowState.PinnedExecutable);
            Assert.Equal(mode.ToString(), finalParent.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            Assert.Equal(mode.ToString(), finalChild.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            finalParent.AssertCompleted(WarningNodeId);
            Assert.Single(finalParent.States(WarningNodeId));
            await using var finalScope = second.Services.CreateAsyncScope();
            foreach (var id in new[] { ParentExecutionId, childId })
            {
                Assert.Empty(await finalScope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(id));
                Assert.Empty(await finalScope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(id));
                Assert.Empty(await finalScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(id));
            }
        }
    }

    private static async Task ResumeCalledWaitAsync(WorkflowExecutionHarness host, string workflowId, string nodeId, string eventName)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var wait = Assert.Single(await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(workflowId),
            candidate => candidate.ExecutableNodeId == nodeId);
        await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new BookmarkResumeDispatchRequest(
            workflowExecutionId: workflowId, stimulusType: wait.StimulusType, stimulusHash: wait.StimulusHash,
            input: JsonSerializer.SerializeToElement(new { eventName }),
            idempotencyKey: workflowId + ":" + nodeId + ":continue", requestedBy: "called-escalation-ef"));
    }

    private async Task DrainCalledAsync(WorkflowExecutionHarness host)
    {
        for (var pass = 0; pass < 12; pass++)
        {
            _clock.Advance(TimeSpan.FromSeconds(2));
            await host.SweepAsync();
        }
    }

    private static RuntimeChildControlTransport ReadControl(RuntimePostCommitIntent intent) =>
        intent.Kind == DispatchWorkflowConstants.ResumeParentIntentKind
            ? intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!.Result.Control!
            : intent.Payload!.Value.Deserialize<RuntimeChildControlTransport>(Json)!;

    private static async Task AssertEscalationStageAsync(WorkflowExecutionHarness host, string childId, WorkflowExecutableIdentity childPin,
        PersistenceMode mode, bool interrupting, bool terminalEmission)
    {
        var parent = await host.ReadRunAsync(ParentExecutionId);
        var child = await host.ReadRunAsync(childId);
        Assert.Equal(WorkflowExecutionHarness.Identity, parent.WorkflowState!.PinnedExecutable);
        Assert.Equal(childPin, child.WorkflowState!.PinnedExecutable);
        Assert.Equal(mode.ToString(), parent.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
        Assert.Equal(mode.ToString(), child.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
        Assert.Equal(ActivityExecutionStatus.Suspended, parent.State(WarningNodeId).Status);
        Assert.Single(parent.States(WarningNodeId));
        Assert.Equal(WorkflowExecutionStatus.Running, parent.WorkflowState.Status);
        Assert.Equal(terminalEmission ? WorkflowExecutionStatus.Completed : interrupting ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Running,
            child.WorkflowState.Status);
        Assert.Equal(terminalEmission ? ActivityExecutionStatus.Completed : interrupting ? ActivityExecutionStatus.Cancelled : ActivityExecutionStatus.Suspended,
            parent.State(DispatchNodeId).Status);
        if (!interrupting && terminalEmission) parent.AssertCompleted(NormalNodeId);
        else parent.AssertDidNotRun(NormalNodeId);
        var inbox = Assert.IsType<RuntimeChildControlInbox>(parent.State(DispatchNodeId).ChildControlInbox);
        Assert.Equal(terminalEmission, inbox.IsTerminalConsumed);
        Assert.Equal(1, inbox.NextOrdinal);
        Assert.Empty(inbox.Pending);
        await using var scope = host.Services.CreateAsyncScope();
        foreach (var id in new[] { ParentExecutionId, childId })
        {
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(id));
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(id));
        }
        var parentWaits = await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(ParentExecutionId);
        Assert.Single(parentWaits, wait => wait.ExecutableNodeId == WarningNodeId);
        var waits = await scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(childId);
        if (!interrupting && !terminalEmission)
            Assert.Equal(ChildWaitNodeId, Assert.Single(waits).ExecutableNodeId);
        else Assert.Empty(waits);
    }

    private static async Task<string> EscalationFactsAsync(WorkflowExecutionHarness host, string childId, WorkflowExecutableIdentity childPin, PersistenceMode mode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var result = new List<object>();
        foreach (var id in new[] { ParentExecutionId, childId })
        {
            var workflow = Assert.IsType<WorkflowExecutionState>(await provider.GetRequiredService<EfWorkflowExecutionStateStore>().FindAsync(id));
            Assert.Equal(id == ParentExecutionId ? WorkflowExecutionHarness.Identity : childPin, workflow.PinnedExecutable);
            Assert.Equal(mode.ToString(), workflow.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            var activities = await provider.GetRequiredService<EfActivityExecutionStateStore>().ListAllAsync(id);
            var incidents = await provider.GetRequiredService<EfIncidentStateStore>().ListAsync(id);
            var bookmarks = provider.GetRequiredService<EfBookmarkStateStore>();
            var physical = new List<BookmarkStateObservation>();
            foreach (var wait in (await bookmarks.ListAllBookmarkStatesAsync(id)).OrderBy(wait => wait.BookmarkId, StringComparer.Ordinal))
                physical.Add(Assert.IsType<BookmarkStateObservation>(await bookmarks.ObserveAsync(id, wait.BookmarkId)));
            result.Add(new { workflow.Status, workflow.PinnedExecutable, mode,
                activities = activities.OrderBy(state => state.InvocationId, StringComparer.Ordinal),
                incidents = incidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal), physical });
        }
        return JsonSerializer.Serialize(result, Json);
    }

    private static async Task<WorkflowExecutable> EscalatingChildAsync(bool terminalEmission)
    {
        await using var builder = await F.CreateAsync([]);
        var graph = terminalEmission
            ? builder.NewExecutable([], [F.StartEvent(), F.EscalationEnd("notice", "WARN")], [F.Flow("emit", "start", "notice")])
            : builder.NewExecutable([F.EventWaitNode(ChildWaitNodeId, "called-continue")],
                [F.StartEvent(), F.EscalationThrow("notice", "WARN"), F.Task("wait", ChildWaitNodeId), F.EndEvent()],
                [F.Flow("emit", "start", "notice"), F.Flow("park", "notice", "wait"), F.Flow("finish", "wait", "end")],
                [F.NodeResumeTarget(ChildWaitNodeId, Event.ResumeTargetId)]);
        return new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-called-child", "definition-called-child", "version-called-child", "1.0.0", "sha256:called-child"),
            graph.RootActivity, graph.ResumeTargets, graph.CreatedAt, graph.CompatibilityMetadata,
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), [], graph.IncidentStrategy);
    }

    private static async Task<WorkflowExecutable> EscalationParentAsync(WorkflowExecutableSourceReference reference, WorkflowExecutableIdentity child, bool interrupting)
    {
        var original = await ParentExecutableAsync(reference, child);
        await using var builder = await F.CreateAsync([]);
        return builder.NewExecutable([original.NodesById[DispatchNodeId], F.EventWaitNode(WarningNodeId, "warning-handler-continue"), builder.NewProbeNode(NormalNodeId)],
            [F.StartEvent(), F.CallActivity("call", DispatchNodeId), F.EscalationBoundary("warning", "call", "WARN", interrupting),
                F.Task("handler", WarningNodeId), F.Task("normal", NormalNodeId), F.EndEvent("normal-end"), F.EndEvent("warning-end")],
            [F.Flow("begin", "start", "call"), F.Flow("normal", "call", "normal"), F.Flow("finish", "normal", "normal-end"),
                F.Flow("warn", "warning", "handler"), F.Flow("warn-end", "handler", "warning-end")],
            [F.NodeResumeTarget(DispatchNodeId, DispatchWorkflowConstants.CompletionResumeTargetId),
                F.NodeResumeTarget(WarningNodeId, Event.ResumeTargetId)], controlCapable: true);
    }
}
