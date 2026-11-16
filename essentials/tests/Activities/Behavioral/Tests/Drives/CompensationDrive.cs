using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Compensation;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>Drives CompensationScope's Done outcome, and its Compensated outcome after a later step faults.</summary>
public sealed class CompensationScopeDrive : IActivityDrive
{
    public Type ActivityType => typeof(CompensationScope);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, WorkflowExecutionHarness.NewProbeNode("node-confirm"), ["actexec-scope", "actexec-reserve", "actexec-confirm"]);
        await RunAsync(recorder, WorkflowExecutionHarness.NewFaultingNode("node-confirm"), ["actexec-scope", "actexec-reserve", "actexec-confirm", "actexec-release"]);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, ExecutableNode confirm, string[] ids)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesCompensationFeature().ConfigureServices(services))
            .Build(ids);
        var root = Nodes.Structural(
            "node-scope",
            typeof(CompensationScope),
            CompensationScope.StructureKind,
            CompensationScope.StructureSchemaVersion,
            new
            {
                steps = new object[]
                {
                    new { name = "Reserve", activity = "node-reserve", compensation = "node-release" },
                    new { name = "Confirm", activity = "node-confirm" }
                }
            },
            [
                new ExecutableChildSlot(CompensationScope.StepSlotName("Reserve"), [WorkflowExecutionHarness.NewProbeNode("node-reserve")]),
                new ExecutableChildSlot(CompensationScope.CompensationSlotName("Reserve"), [WorkflowExecutionHarness.NewProbeNode("node-release")]),
                new ExecutableChildSlot(CompensationScope.StepSlotName("Confirm"), [confirm])
            ],
            new Dictionary<string, object?> { ["faultAfterCompensation"] = false });

        recorder.Record(ActivityType, await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root)), "node-scope");
    }
}
