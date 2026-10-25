using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnCombinedOutcomeTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Ordered_signals_then_original_fault_commit_one_verdict_without_leaking_unissued_work(bool interrupting, bool routingFault)
    {
        await using var host = await F.CreateAsync(Enumerable.Range(0, 40).Select(index => $"combined-{index}"));
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
        }
        else if (interrupting)
        {
            run.AssertWorkflowCompleted();
            run.AssertCompleted(F.ProcessNodeId);
            Assert.Single(run.States("node-handler")); // The second signal for the retired handle is consumed.
            Assert.Equal(IncidentStatus.Resolved, original.Status);
        }
        else
        {
            Assert.Equal(ActivityExecutionStatus.Faulted, run.State(F.ProcessNodeId).Status);
            Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState!.Status);
            run.AssertDidNotRun("node-handler"); // Starts held by both signal evaluations are withdrawn on final Fault.
            Assert.NotEqual(IncidentStatus.Resolved, original.Status);
        }
        Assert.Empty(await host.BookmarksAsync());
    }
}

public sealed class SignallingFaultActivity : StructuralActivity, IRuntimeStructuralActivity
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var escalation = JsonSerializer.SerializeToElement(new { code = "notice" });
        context.RequestParentNotification("bpmn.escalation", escalation);
        context.RequestParentNotification("bpmn.escalation", escalation);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.bpmn.original", "original child fault")));
    }
}
