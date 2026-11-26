using System.Text.Json;
using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnCombinedOutcomeTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Ordered_signals_then_original_fault_commit_one_verdict_without_leaking_unissued_work(bool interrupting, bool routingFault)
    {
        var outcomes = new ConcurrentQueue<ObservedCombinedOutcome>();
        var applications = new ConcurrentQueue<ObservedCommandApplication>();
        await using var host = await CreateObservedHostAsync(outcomes, applications);
        var child = new ExecutableNode("node-reporter", "authored-reporter", typeof(SignallingFaultActivity).FullName!, "1.0.0",
            "test/clr", JsonSerializer.SerializeToElement(new { type = "clr" }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var graph = host.NewExecutable(routingFault ? [child] : [child, host.NewProbeNode("node-handler")],
            [F.StartEvent(), F.SubProcess("host", "node-reporter"), F.EscalationBoundary("boundary", "host", "notice", interrupting),
                routingFault ? F.ExclusiveGateway("handler") : F.Task("handler", "node-handler"),
                F.EndEvent("host-end"), F.EndEvent("handler-end"), F.EndEvent("other-end")],
            [F.Flow("start-host", "start", "host"), F.Flow("host-end", "host", "host-end"),
                F.Flow("boundary-handler", "boundary", "handler"),
                F.Flow("handler-end", "handler", "handler-end", routingFault ? "Left" : null),
                F.Flow("handler-other", "handler", "other-end", "Right")]);

        var run = await host.RunAsync(graph);

        var application = Assert.Single(applications);
        Assert.Equal(routingFault || !interrupting ? RuntimeStructuralContinuationKind.Fault : RuntimeStructuralContinuationKind.Defer, application.Verdict);
        Assert.Equal(interrupting && !routingFault ? new[] { "node-handler" } : Array.Empty<string>(), application.ScheduledNodes);
        Assert.Equal(0, application.CancellationCount);
        Assert.Single(outcomes);
        var reporter = run.State("node-reporter");
        Assert.Equal(ActivityExecutionStatus.Faulted, reporter.Status);
        Assert.Equal("test.bpmn.original", reporter.Fault!.Code);
        Assert.Equal("original child fault", reporter.Fault.Message);
        var incidents = await host.Provider.GetRequiredService<IIncidentStateStore>().ListAsync("wfexec-1");
        var original = Assert.Single(incidents, incident => incident.ExecutableNodeId == "node-reporter");
        if (routingFault)
        {
            Assert.Equal("bpmn.gateway.no-flow-selected", run.State(F.ProcessNodeId).Fault!.Code);
            run.AssertDidNotRun("node-handler");
            Assert.NotEqual(IncidentStatus.Resolved, original.Status);
            Assert.NotEqual(IncidentResolutionActionKinds.AbsorbFault, original.ResolutionOutcome?.ActionKind);
        }
        else if (interrupting)
        {
            run.AssertWorkflowCompleted();
            run.AssertCompleted(F.ProcessNodeId);
            Assert.Single(run.States("node-handler")); // The second signal for the retired handle is consumed.
            Assert.Equal(IncidentStatus.Resolved, original.Status);
            Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, original.ResolutionOutcome!.ActionKind);
            Assert.Equal(IncidentResolutionSystemSources.StructuralFaultAbsorption, original.ResolutionOutcome.SystemSource);
        }
        else
        {
            Assert.Equal(ActivityExecutionStatus.Faulted, run.State(F.ProcessNodeId).Status);
            Assert.Equal("bpmn.work.faulted", run.State(F.ProcessNodeId).Fault!.Code);
            Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState!.Status);
            run.AssertDidNotRun("node-handler"); // Starts held by both signal evaluations are withdrawn on final Fault.
            Assert.NotEqual(IncidentStatus.Resolved, original.Status);
            Assert.NotEqual(IncidentResolutionActionKinds.AbsorbFault, original.ResolutionOutcome?.ActionKind);
        }
        Assert.Empty(await host.BookmarksAsync());
    }
}

public sealed class SignallingFaultActivity : StructuralActivity, IRuntimeStructuralActivity
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        for (var ordinal = 1; ordinal <= 2; ordinal++)
            context.RequestParentNotification("bpmn.escalation", JsonSerializer.SerializeToElement(new { code = "notice", ordinal }));
        return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.bpmn.original", "original child fault")));
    }
}
