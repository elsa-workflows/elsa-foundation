using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>Flowchart: Done when a path runs to the end, Break when any path reaches a Break (#304).</summary>
public sealed class FlowchartDrive : IActivityDrive
{
    public Type ActivityType => typeof(FlowchartActivity);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, breakOut: false, ["actexec-flowchart", "actexec-a", "actexec-b"]);
        await RunAsync(recorder, breakOut: true, ["actexec-flowchart", "actexec-a"]);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, bool breakOut, string[] ids)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesFlowchartRuntimeFeature().ConfigureServices(services))
            .Build(ids);

        var first = breakOut
            ? WorkflowExecutionHarness.NewProbeNode("node-a", ["Break"])
            : WorkflowExecutionHarness.NewProbeNode("node-a");
        var second = WorkflowExecutionHarness.NewProbeNode("node-b");
        var root = Nodes.Structural(
            "node-flowchart",
            typeof(FlowchartActivity),
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure([new FlowchartConnection(new FlowchartEndpoint("node-a", null), new FlowchartEndpoint("node-b"))]),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [first, second])]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));
        recorder.Record(ActivityType, run, "node-flowchart");

        if (breakOut)
            run.AssertDidNotRun("node-b");
    }
}
