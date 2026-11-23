using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Compensation;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>Drives Confirm's Done outcome as the last step of a compensation scope.</summary>
public sealed class ConfirmDrive : IActivityDrive
{
    public Type ActivityType => typeof(Confirm);

    public Task DriveAsync(ActivityDriveRecorder recorder) =>
        CompensationRequestDrive.RunAsync(recorder, ActivityType, ["actexec-scope", "actexec-reserve", "actexec-request"]);
}

/// <summary>Drives Compensate's Done outcome as the last step of a compensation scope, which then undoes the first step.</summary>
public sealed class CompensateDrive : IActivityDrive
{
    public Type ActivityType => typeof(Compensate);

    public Task DriveAsync(ActivityDriveRecorder recorder) =>
        CompensationRequestDrive.RunAsync(recorder, ActivityType, ["actexec-scope", "actexec-reserve", "actexec-request", "actexec-release"]);
}

internal static class CompensationRequestDrive
{
    public static async Task RunAsync(ActivityDriveRecorder recorder, Type requestType, string[] ids)
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
                    new { name = "Request", activity = "node-request" }
                }
            },
            [
                new ExecutableChildSlot(CompensationScope.StepSlotName("Reserve"), [WorkflowExecutionHarness.NewProbeNode("node-reserve")]),
                new ExecutableChildSlot(CompensationScope.CompensationSlotName("Reserve"), [WorkflowExecutionHarness.NewProbeNode("node-release")]),
                new ExecutableChildSlot(CompensationScope.StepSlotName("Request"), [Nodes.Leaf("node-request", requestType)])
            ],
            new Dictionary<string, object?> { ["faultAfterCompensation"] = false });

        recorder.Record(requestType, await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root)), "node-request");
    }
}
