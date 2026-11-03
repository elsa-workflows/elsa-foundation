using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnGraphAdapterTests
{
    [Fact]
    public async Task Complete_child_inventory_rejects_an_orphan_on_the_actual_runtime_path()
    {
        await using var host = await F.CreateAsync(["orphan-parent"]);
        var executable = host.NewExecutable([host.NewProbeNode("node-orphan")],
            [F.StartEvent(), F.EndEvent()], [F.Flow("start-end", "start", "end")]);

        var boundary = Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(executable.RootActivity));
        Assert.Contains(F.ProcessNodeId, boundary.Message, StringComparison.Ordinal);
        var original = Assert.IsType<global::Bpmn.Semantics.BpmnExecutionException>(boundary.InnerException);
        Assert.Contains("node-orphan", original.Message, StringComparison.Ordinal);
        var run = await host.RunAsync(executable);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(F.ProcessNodeId).Status);
        Assert.Equal(typeof(BpmnHostException).FullName, run.State(F.ProcessNodeId).Fault!.ExceptionType);
        Assert.Contains(F.ProcessNodeId, run.State(F.ProcessNodeId).Fault!.Message, StringComparison.Ordinal);
        run.AssertDidNotRun("node-orphan");
    }
}
