using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnCombinedOutcomeTests
{
    [Fact]
    public async Task Interrupting_combined_outcome_preserves_other_same_node_null_iteration_invocation_and_exact_wait()
    {
        var observed = new ConcurrentQueue<ObservedCombinedOutcome>();
        await using var host = await CreateObservedHostAsync(observed);
        var waitNode = F.EventWaitNode("node-shared-wait", "continue");
        var reporter = new ExecutableNode("node-shared-reporter", "authored-shared", typeof(DeferredSignallingFaultActivity).FullName!, "1.0.0",
            "test/clr", JsonSerializer.SerializeToElement(new { type = "clr" }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot("Reporter.Children", [waitNode])]);
        var graph = host.NewExecutable([reporter, host.NewProbeNode("node-handler")],
            [F.StartEvent(), F.ParallelGateway("fork"), F.SubProcess("host", reporter.ExecutableNodeId),
                F.EscalationBoundary("boundary", "host", "notice", true), F.Task("handler", "node-handler"),
                F.EndEvent("host-end"), F.EndEvent("handler-end")],
            [F.Flow("start-fork", "start", "fork"), F.Flow("fork-left", "fork", "host"), F.Flow("fork-right", "fork", "host"),
                F.Flow("host-end", "host", "host-end"), F.Flow("boundary-handler", "boundary", "handler"), F.Flow("handler-end", "handler", "handler-end")],
            [F.NodeResumeTarget(waitNode.ExecutableNodeId, Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)]);
        var started = await host.RunAsync(graph);
        Assert.True(started.WorkflowState!.Status == WorkflowExecutionStatus.Running,
            JsonSerializer.Serialize(started.ActivityStates.Select(state => new { state.InvocationId, state.Execution.ExecutableNodeId, state.Status, state.Fault })));
        var store = host.Provider.GetRequiredService<IActivityExecutionStateStore>();
        var before = await store.ListAllAsync("wfexec-1");
        var reporters = before.Where(state => state.Execution.ExecutableNodeId == reporter.ExecutableNodeId)
            .OrderBy(state => state.InvocationId, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, reporters.Length);
        var other = reporters[0]; var selected = reporters[1];
        Assert.Equal("host", selected.Provenance.Metadata.GetValueOrDefault("bpmn.elementId"));
        Assert.Equal("host", other.Provenance.Metadata.GetValueOrDefault("bpmn.elementId"));
        Assert.Equal(other.ParentActivityExecutionId, selected.ParentActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Running, selected.Status);
        Assert.Equal(ActivityExecutionStatus.Running, other.Status);
        Assert.Null(selected.IterationId); Assert.Null(other.IterationId);
        Assert.NotEqual(selected.InvocationId, other.InvocationId);
        Assert.False(string.IsNullOrWhiteSpace(selected.Provenance.ExecutionPathId));
        Assert.False(string.IsNullOrWhiteSpace(other.Provenance.ExecutionPathId));
        Assert.NotEqual(selected.Provenance.ExecutionPathId, other.Provenance.ExecutionPathId);
        Assert.True(StringComparer.Ordinal.Compare(other.InvocationId, selected.InvocationId) < 0,
            "The selected invocation must not be the first same-node/null-iteration history match.");
        var parentBefore = await host.GetBpmnStateAsync();
        Assert.Equal(2, parentBefore.ActiveWork.Count(work => work.NodeId == reporter.ExecutableNodeId));
        Assert.Contains(parentBefore.ActiveWork, work => work.TokenId == selected.Provenance.ExecutionPathId);
        Assert.Contains(parentBefore.ActiveWork, work => work.TokenId == other.Provenance.ExecutionPathId);
        var waits = await host.BookmarksAsync();
        Assert.Equal(2, waits.Count);
        var selectedWait = Assert.Single(waits, wait => before.Single(state => state.InvocationId == wait.ActivityExecutionId).ParentActivityExecutionId == selected.InvocationId);
        var otherWait = Assert.Single(waits, wait => before.Single(state => state.InvocationId == wait.ActivityExecutionId).ParentActivityExecutionId == other.InvocationId);
        var otherLeaf = Assert.Single(before, state => state.InvocationId == otherWait.ActivityExecutionId);
        var bookmarks = host.Provider.GetRequiredService<IBookmarkStateStore>();
        var originalWait = Assert.IsType<BookmarkStateObservation>(await bookmarks.ObserveAsync("wfexec-1", otherWait.BookmarkId));

        var otherFacts = JsonSerializer.SerializeToElement(other);
        var otherLeafFacts = JsonSerializer.SerializeToElement(otherLeaf);
        var otherWaitFacts = JsonSerializer.SerializeToElement(originalWait);
        await host.ResumeAsync(selectedWait, JsonSerializer.SerializeToElement(new { eventName = "continue" }));

        var after = await store.ListAllAsync("wfexec-1");
        AssertSameDurableFacts(otherFacts, Assert.Single(after, state => state.InvocationId == other.InvocationId));
        AssertSameDurableFacts(otherLeafFacts, Assert.Single(after, state => state.InvocationId == otherLeaf.InvocationId));
        var surviving = Assert.Single(await host.BookmarksAsync());
        Assert.Equal(otherWait.BookmarkId, surviving.BookmarkId);
        Assert.True(JsonElement.DeepEquals(otherWaitFacts,
            JsonSerializer.SerializeToElement(await bookmarks.ObserveAsync("wfexec-1", otherWait.BookmarkId))));
        var faulted = Assert.Single(after, state => state.InvocationId == selected.InvocationId);
        Assert.Equal(ActivityExecutionStatus.Faulted, faulted.Status);
        Assert.Equal("test.bpmn.deferred-original", faulted.Fault!.Code);
        Assert.Single(after, state => state.Execution.ExecutableNodeId == "node-handler" && state.Status == ActivityExecutionStatus.Completed);
        var parentAfter = await host.GetBpmnStateAsync();
        Assert.DoesNotContain(parentAfter.Tokens, token => token.TokenId == selected.Provenance.ExecutionPathId);
        Assert.Single(parentAfter.ActiveWork, work => work.TokenId == other.Provenance.ExecutionPathId);
        var outcome = Assert.Single(observed);
        Assert.Equal(selected.InvocationId, outcome.ChildId);
        Assert.Equal(F.ProcessNodeId, outcome.ParentNodeId);
        Assert.Equal(["bpmn.escalation", "bpmn.escalation"], outcome.Codes);
        Assert.Equal([1, 2], outcome.Payloads.Select(payload => JsonSerializer.Deserialize<JsonElement>(payload).GetProperty("ordinal").GetInt32()));
        var incident = Assert.Single(await host.Provider.GetRequiredService<IIncidentStateStore>().ListAsync("wfexec-1"));
        Assert.Equal(outcome.IncidentId, incident.IncidentId);
        Assert.Equal(selected.InvocationId, incident.ActivityExecutionId);
        Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, incident.ResolutionOutcome!.ActionKind);
        Assert.False(incident.IsBlocking);
        Assert.Equal(IncidentResolutionSystemSources.StructuralFaultAbsorption, incident.ResolutionOutcome.SystemSource);
        Assert.Equal(Assert.Single(after, state => state.Execution.ExecutableNodeId == F.ProcessNodeId).InvocationId,
            incident.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FaultAbsorbedBy));
        Assert.Equal(WorkflowExecutionStatus.Running, (await host.Provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1"))!.Status);

        // The untouched sibling still executes its own real outcome and its own scoped catcher.
        // It must not reuse the first invocation's handler or absorbed incident.
        await host.ResumeAsync(surviving, JsonSerializer.SerializeToElement(new { eventName = "continue" }));
        Assert.Equal(2, observed.Count);
        Assert.Equal(other.InvocationId, observed.Last().ChildId);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await host.Provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1"))!.Status);
        Assert.Empty(await host.BookmarksAsync());
        var finalIncidents = await host.Provider.GetRequiredService<IIncidentStateStore>().ListAsync("wfexec-1");
        Assert.False(Assert.Single(finalIncidents, candidate => candidate.IncidentId == incident.IncidentId).IsBlocking);
        var otherIncident = Assert.Single(finalIncidents, candidate => candidate.ActivityExecutionId == other.InvocationId);
        Assert.NotEqual(incident.IncidentId, otherIncident.IncidentId);
        Assert.False(otherIncident.IsBlocking);
        Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, otherIncident.ResolutionOutcome!.ActionKind);
        Assert.Equal(2, (await store.ListAllAsync("wfexec-1")).Count(state => state.Execution.ExecutableNodeId == "node-handler" && state.Status == ActivityExecutionStatus.Completed));
    }

    [Fact]
    public async Task Outward_ordered_notifications_survive_inner_final_fault_and_reach_outer_bpmn_combined_outcome()
    {
        var observed = new ConcurrentQueue<ObservedCombinedOutcome>();
        await using var host = await CreateObservedHostAsync(observed);
        var reporter = new ExecutableNode("node-inner-reporter", "authored-inner-reporter", typeof(SignallingFaultActivity).FullName!, "1.0.0",
            "test/clr", JsonSerializer.SerializeToElement(new { type = "clr" }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var inner = F.NestedProcessNode("node-inner",
            elements: [F.StartEvent(), F.SubProcess("report", reporter.ExecutableNodeId), F.EndEvent()],
            flows: [F.Flow("start-report", "start", "report"), F.Flow("report-end", "report", "end")], innerChildren: [reporter]);
        var graph = host.NewExecutable([inner, host.NewProbeNode("node-outer-handler")],
            [F.StartEvent(), F.SubProcess("inner", inner.ExecutableNodeId), F.EscalationBoundary("boundary", "inner", "notice", true),
                F.Task("handler", "node-outer-handler"), F.EndEvent("inner-end"), F.EndEvent("handler-end")],
            [F.Flow("start-inner", "start", "inner"), F.Flow("inner-end", "inner", "inner-end"),
                F.Flow("boundary-handler", "boundary", "handler"), F.Flow("handler-end", "handler", "handler-end")]);

        var run = await host.RunAsync(graph);

        run.AssertWorkflowCompleted();
        run.AssertCompleted(F.ProcessNodeId);
        Assert.Single(run.States("node-outer-handler"));
        var innerOutcome = Assert.Single(observed, outcome => outcome.ParentNodeId == inner.ExecutableNodeId);
        var outerOutcome = Assert.Single(observed, outcome => outcome.ParentNodeId == F.ProcessNodeId);
        Assert.Equal(run.State(reporter.ExecutableNodeId).InvocationId, innerOutcome.ChildId);
        Assert.Equal(run.State(inner.ExecutableNodeId).InvocationId, outerOutcome.ChildId);
        Assert.Equal("test.bpmn.original", innerOutcome.FaultCode);
        Assert.Equal("bpmn.work.faulted", outerOutcome.FaultCode);
        Assert.Equal(2, innerOutcome.Payloads.Count);
        Assert.Equal(["bpmn.escalation", "bpmn.escalation"], innerOutcome.Codes);
        Assert.Equal(innerOutcome.Codes, outerOutcome.Codes);
        Assert.Equal(innerOutcome.Payloads, outerOutcome.Payloads);
        Assert.Equal([1, 2], outerOutcome.Payloads.Select(payload => JsonSerializer.Deserialize<JsonElement>(payload).GetProperty("ordinal").GetInt32()));
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(inner.ExecutableNodeId).Status);
        Assert.Equal("test.bpmn.original", run.State(reporter.ExecutableNodeId).Fault!.Code);
        var incidents = await host.Provider.GetRequiredService<IIncidentStateStore>().ListAsync("wfexec-1");
        var absorbed = Assert.Single(incidents, incident => incident.IncidentId == outerOutcome.IncidentId);
        Assert.Equal(run.State(inner.ExecutableNodeId).InvocationId, absorbed.ActivityExecutionId);
        Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, absorbed.ResolutionOutcome!.ActionKind);
        Assert.False(absorbed.IsBlocking);
        Assert.DoesNotContain(incidents, incident => incident.IsBlocking);
        Assert.Empty(await host.BookmarksAsync());
    }

    private static void AssertSameDurableFacts(JsonElement expected, ActivityExecutionState actual) =>
        Assert.True(JsonElement.DeepEquals(expected, JsonSerializer.SerializeToElement(actual)),
            "The combined outcome changed another invocation's complete durable facts.");

    private static async Task<F> CreateObservedHostAsync(ConcurrentQueue<ObservedCombinedOutcome> observations,
        ConcurrentQueue<ObservedCommandApplication>? applications = null) =>
        await F.CreateAsync(Enumerable.Range(0, 80).Select(index => $"combined-scope-{index:D2}"), services =>
        {
            var original = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBpmnProcessRuntime));
            services.Remove(original);
            services.AddScoped<IBpmnProcessRuntime>(provider => new ObservedCombinedOutcomeRuntime(
                (IBpmnProcessRuntime)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), observations, applications));
        });

    private sealed record ObservedCombinedOutcome(string ParentNodeId, string ChildId, string FaultCode, string IncidentId, IReadOnlyList<string> Codes, IReadOnlyList<string> Payloads);
    private sealed record ObservedCommandApplication(RuntimeStructuralContinuationKind Verdict, IReadOnlyList<string> ScheduledNodes, int CancellationCount);
    private sealed class ObservedCombinedOutcomeRuntime(IBpmnProcessRuntime inner, ConcurrentQueue<ObservedCombinedOutcome> observations,
        ConcurrentQueue<ObservedCommandApplication>? applications) : IBpmnProcessRuntime
    {
        public ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context) => inner.StartAsync(context);
        public ValueTask<RuntimeStructuralContinuation> CompleteAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completion) => inner.CompleteAsync(context, completion);
        public ValueTask<RuntimeStructuralContinuation> FaultAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext fault) => inner.FaultAsync(context, fault);
        public ValueTask<RuntimeStructuralContinuation> SignalAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext signal) => inner.SignalAsync(context, signal);
        public ValueTask<RuntimeStructuralContinuation> ControlAsync(IRuntimeActivityExecutionContext context, RuntimeChildControlContext control) => inner.ControlAsync(context, control);
        public async ValueTask<RuntimeStructuralContinuation> OutcomeAsync(IRuntimeActivityExecutionContext context, ActivityChildOutcomeContext outcome)
        {
            observations.Enqueue(new(context.ExecutableNode.ExecutableNodeId, outcome.FaultedChildActivityExecutionId, outcome.Fault.Code,
                outcome.IncidentId, outcome.Notifications.Select(notification => notification.Code).ToArray(),
                outcome.Notifications.Select(notification => notification.Payload!.Value.GetRawText()).ToArray()));
            var result = await inner.OutcomeAsync(context, outcome);
            applications?.Enqueue(new(result.Kind, context.GetChildActivityScheduleRequests().Select(request => request.ExecutableNodeId).ToArray(),
                context.GetChildSubtreeCancellationRequests().Count));
            return result;
        }
    }
}

public sealed class DeferredSignallingFaultActivity : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        context.ScheduleChildActivity(context.ExecutableNode.ChildSlots.Single().Activities.Single().ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        var runtime = (IRuntimeActivityExecutionContext)context.ParentContext;
        for (var ordinal = 1; ordinal <= 2; ordinal++)
            runtime.RequestParentNotification("bpmn.escalation", JsonSerializer.SerializeToElement(new { code = "notice", ordinal }));
        return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.bpmn.deferred-original", "original deferred child fault")));
    }
}
