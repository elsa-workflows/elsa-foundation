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

        Assert.Throws<BpmnHostException>(() => BpmnGraphAdapter.Build(executable.RootActivity));
        var run = await host.RunAsync(executable);

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(F.ProcessNodeId).Status);
        run.AssertDidNotRun("node-orphan");
    }
}
