using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>
/// Shared wiring for the leaf primitives. Each one is driven as the single child of a Sequence so the leaf runs
/// through the real scheduler exactly as it would in an authored graph, rather than as a root special case.
/// </summary>
public abstract class PrimitiveDrive : IActivityDrive
{
    public abstract Type ActivityType { get; }

    public abstract Task DriveAsync(ActivityDriveRecorder recorder);

    protected static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceRuntimeFeature().ConfigureServices(services))
            .Build(activityExecutionIds);

    /// <summary>Wraps children in a Sequence root so a leaf is scheduled as a child, not executed as the root.</summary>
    protected static WorkflowExecutable Sequence(params ExecutableNode[] children) =>
        WorkflowExecutionHarness.NewExecutable(Nodes.Structural(
            "node-sequence",
            typeof(SequenceActivity),
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            new { activities = children.Select(child => child.ExecutableNodeId).ToArray() },
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)]));

    /// <summary>Drives one leaf inside a Sequence and records what the engine committed for it.</summary>
    protected async Task<WorkflowExecutionRun> RunLeafAsync(
        ActivityDriveRecorder recorder,
        ExecutableNode leaf,
        params string[] activityExecutionIds)
    {
        await using var harness = NewHarness(activityExecutionIds);
        var run = await harness.RunAsync(Sequence(leaf));
        recorder.Record(ActivityType, run, leaf.ExecutableNodeId);
        return run;
    }
}

/// <summary>
/// Sequence itself: Done when its children finish, Break when a child breaks out. Both are driven through the
/// real scheduler with probe children.
/// </summary>
public sealed class SequenceDrive : IActivityDrive
{
    public Type ActivityType => typeof(SequenceActivity);

    public async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, breakOut: false, ["actexec-seq", "actexec-a", "actexec-b"]);
        await RunAsync(recorder, breakOut: true, ["actexec-seq", "actexec-a"]);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, bool breakOut, string[] ids)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceRuntimeFeature().ConfigureServices(services))
            .Build(ids);

        var first = breakOut
            ? WorkflowExecutionHarness.NewProbeNode("node-a", ["Break"])
            : WorkflowExecutionHarness.NewProbeNode("node-a");
        var second = WorkflowExecutionHarness.NewProbeNode("node-b");
        var root = Nodes.Structural(
            "node-sequence",
            typeof(SequenceActivity),
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            new { activities = new[] { first.ExecutableNodeId, second.ExecutableNodeId } },
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [first, second])]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));
        recorder.Record(ActivityType, run, "node-sequence");

        // A Break in the first child must stop the sequence before the second child runs.
        if (breakOut)
            run.AssertDidNotRun("node-b");
    }
}

/// <summary>WriteLine: writes its required Text input and completes Done.</summary>
public sealed class WriteLineDrive : PrimitiveDrive
{
    public override Type ActivityType => typeof(WriteLine);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        var leaf = Nodes.Leaf("node-writeline", typeof(WriteLine), new Dictionary<string, object?> { ["Text"] = "driven" });
        var run = await RunLeafAsync(recorder, leaf, "actexec-seq", "actexec-writeline");
        run.AssertWorkflowCompleted();
    }
}

/// <summary>WriteLines: a null collection writes nothing and still completes, and a real collection writes each line.</summary>
public sealed class WriteLinesDrive : PrimitiveDrive
{
    public override Type ActivityType => typeof(WriteLines);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunLeafAsync(
            recorder,
            Nodes.Leaf("node-writelines", typeof(WriteLines), new Dictionary<string, object?> { ["Lines"] = new[] { "one", "two" } }),
            "actexec-seq", "actexec-writelines");

        await RunLeafAsync(
            recorder,
            Nodes.Leaf("node-writelines", typeof(WriteLines), new Dictionary<string, object?> { ["Lines"] = null }),
            "actexec-seq", "actexec-writelines");
    }
}

/// <summary>
/// ReadLine: reads one line from stdin. The drive redirects stdin so the Line output is genuinely populated —
/// under the default (closed) test stdin, <c>Console.ReadLine</c> returns null and the output would look dead.
/// </summary>
public sealed class ReadLineDrive : PrimitiveDrive
{
    public override Type ActivityType => typeof(ReadLine);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        var original = Console.In;
        try
        {
            Console.SetIn(new StringReader("a line from stdin"));
            var run = await RunLeafAsync(
                recorder,
                Nodes.Leaf("node-readline", typeof(ReadLine)),
                "actexec-seq", "actexec-readline");
            run.AssertWorkflowCompleted();
        }
        finally
        {
            Console.SetIn(original);
        }
    }
}

/// <summary>Inline: the materialized expression value flows straight through as the activity result.</summary>
public sealed class InlineDrive : PrimitiveDrive
{
    public override Type ActivityType => typeof(Inline);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        var leaf = Nodes.Leaf("node-inline", typeof(Inline), new Dictionary<string, object?> { ["Expression"] = "inline value" });
        var run = await RunLeafAsync(recorder, leaf, "actexec-seq", "actexec-inline");
        run.AssertWorkflowCompleted();
    }
}

/// <summary>Break: a leaf whose whole purpose is emitting the Break outcome to its enclosing container.</summary>
public sealed class BreakDrive : PrimitiveDrive
{
    public override Type ActivityType => typeof(Break);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        // Inside a Sequence, so the Break has a real container to propagate to.
        await using var harness = NewHarness("actexec-seq", "actexec-break");
        var breakNode = Nodes.Leaf("node-break", typeof(Break));
        var after = WorkflowExecutionHarness.NewProbeNode("node-after");
        var root = Nodes.Structural(
            "node-sequence",
            typeof(SequenceActivity),
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            new { activities = new[] { breakNode.ExecutableNodeId, after.ExecutableNodeId } },
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [breakNode, after])]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));
        recorder.Record(ActivityType, run, "node-break");
        run.AssertDidNotRun("node-after");
    }
}

/// <summary>
/// Fault: a deliberate typed fault. There is no completion outcome to reach — the activity's contract is that it
/// never completes — so the drive records the run and asserts the fault landed with the authored classification.
/// </summary>
public sealed class FaultDrive : PrimitiveDrive
{
    public override Type ActivityType => typeof(Fault);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = NewHarness("actexec-seq", "actexec-fault");
        var leaf = Nodes.Leaf("node-fault", typeof(Fault), new Dictionary<string, object?>
        {
            ["code"] = "order.rejected",
            ["message"] = "Payment declined.",
            ["category"] = "Payment",
            ["faultType"] = "Business"
        });

        var run = await harness.RunAsync(Sequence(leaf));
        recorder.RecordDriven(ActivityType);

        var state = run.State("node-fault");
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("order.rejected", state.Fault!.Code);
        Assert.Equal("Payment declined.", state.Fault.Message);
        Assert.Equal("Payment", state.Fault.Metadata[Runtime.Core.Models.ActivityFault.CategoryMetadataKey]);
        Assert.Equal("Business", state.Fault.Metadata[Runtime.Core.Models.ActivityFault.FaultTypeMetadataKey]);
    }
}

/// <summary>
/// PublishEvent: stages the send and completes Done immediately (fire-and-continue). The staged request is
/// asserted so the drive proves the send actually happened, not just that the node completed.
/// </summary>
public sealed class PublishEventDrive : PrimitiveDrive
{
    public override Type ActivityType => typeof(PublishEvent);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .Build("actexec-seq", "actexec-publish");

        var leaf = Nodes.Leaf("node-publish", typeof(PublishEvent), new Dictionary<string, object?>
        {
            ["EventName"] = "order.placed",
            ["IsLocalEvent"] = false
        });

        var run = await harness.RunAsync(Sequence(leaf));
        recorder.Record(ActivityType, run, "node-publish");
        run.AssertWorkflowCompleted();
    }
}

/// <summary>
/// Event as a mid-flow catch: the first run suspends on a bookmark, the resume delivers the payload and completes.
/// Driving the resume is what proves the Result output is genuinely populated rather than merely declared.
/// </summary>
public sealed class EventDrive : PrimitiveDrive
{
    public override Type ActivityType => typeof(Event);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .Build("actexec-seq", "actexec-event");

        const string executableNodeId = "node-event";
        const string eventName = "order.shipped";
        var resumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(executableNodeId, Event.ResumeTargetId);
        var leaf = Nodes.Leaf(executableNodeId, typeof(Event), new Dictionary<string, object?>
        {
            ["EventName"] = eventName,
            ["CanStartWorkflow"] = false
        });

        var root = Nodes.Structural(
            "node-sequence",
            typeof(SequenceActivity),
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            new { activities = new[] { executableNodeId } },
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [leaf])]);

        var executable = new WorkflowExecutable(
            WorkflowExecutionHarness.Identity,
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [resumeTargetId] = new(resumeTargetId, executableNodeId, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId)
            },
            new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);

        var suspended = await harness.RunAsync(executable);
        var waiting = suspended.State(executableNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, waiting.Status);

        var resumed = await harness.ResumeAsync(
            WorkflowExecutionHarness.Identity,
            bookmarkId: Assert.Single(waiting.BookmarkIds),
            activityExecutionId: waiting.InvocationId,
            executableNodeId: executableNodeId,
            resumeTargetId: resumeTargetId,
            stimulusType: EventStimulus.StimulusType,
            stimulusHash: EventStimulus.Hash(eventName),
            input: JsonSerializer.SerializeToElement(new EventReceived(eventName)));

        recorder.Record(ActivityType, resumed, executableNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, resumed.State(executableNodeId).Status);
    }
}
