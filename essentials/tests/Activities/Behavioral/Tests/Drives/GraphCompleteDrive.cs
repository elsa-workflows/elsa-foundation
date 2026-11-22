using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Graph.Runtime.Activities;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Testing;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// Complete: ends its nearest graph boundary from inside a Sequence body. The node itself completes with the
/// implicit Done (nothing ever routes from it); the boundary's chosen outcome is asserted in
/// <c>GraphBoundaryCompleteTests</c>.
/// </summary>
public sealed class GraphCompleteDrive : IActivityDrive
{
    public Type ActivityType => typeof(Complete);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = GraphBoundaryCompleteTests.NewHarness();
        var boundary = GraphBoundaryCompleteTests.Boundary(
            GraphBoundaryCompleteTests.Sequence(
                "node-body",
                GraphBoundaryCompleteTests.CompleteNode("node-complete", "fail"),
                GraphBoundaryCompleteTests.Probe("node-never")),
            completeOutcomes: [new GraphActivityCompleteOutcomeDescriptor("fail", "Failed")]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(boundary));
        recorder.Record(typeof(Complete), run, "node-complete");
        run.AssertDidNotRun("node-never");
    }
}
