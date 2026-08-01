using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using DoActivity = Elsa.Activities.Do.Activities.Do;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using IfActivity = Elsa.Activities.If.Activities.If;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Behavioral.Drives;

/// <summary>Shared harness wiring for the control-flow composites; they all need the same two features.</summary>
public abstract class ControlFlowDrive : IActivityDrive
{
    public abstract Type ActivityType { get; }

    public abstract Task DriveAsync(ActivityDriveRecorder recorder);

    protected static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .Build(activityExecutionIds);

    /// <summary>A probe leaf that completes with the given outcome — how a body signals Break to its loop.</summary>
    protected static ExecutableNode Probe(string nodeId, string? outcome = null) =>
        outcome is null
            ? WorkflowExecutionHarness.NewProbeNode(nodeId)
            : WorkflowExecutionHarness.NewProbeNode(nodeId, [outcome]);
}

/// <summary>Drives all three declared If outcomes: the true branch, the false branch, and a body Break.</summary>
public sealed class IfDrive : ControlFlowDrive
{
    public override Type ActivityType => typeof(IfActivity);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, condition: true, thenOutcome: null, ["actexec-if", "actexec-then"]);
        await RunAsync(recorder, condition: false, thenOutcome: null, ["actexec-if", "actexec-else"]);
        // A branch that breaks propagates Break out of the composite (#261/#299).
        await RunAsync(recorder, condition: true, thenOutcome: "Break", ["actexec-if", "actexec-then"]);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, bool condition, string? thenOutcome, string[] ids)
    {
        await using var harness = NewHarness(ids);
        var root = Nodes.Structural(
            "node-if",
            typeof(IfActivity),
            IfActivity.StructureKind,
            IfActivity.StructureSchemaVersion,
            new { then = "node-then", @else = "node-else" },
            [
                new ExecutableChildSlot(IfActivity.ThenSlotName, [Probe("node-then", thenOutcome)]),
                new ExecutableChildSlot(IfActivity.ElseSlotName, [Probe("node-else")])
            ],
            new Dictionary<string, object?> { ["Condition"] = condition });

        recorder.Record(ActivityType, await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root)), "node-if");
    }
}

/// <summary>Drives Switch's declared Default and Break outcomes, plus an authored case port.</summary>
public sealed class SwitchDrive : ControlFlowDrive
{
    public override Type ActivityType => typeof(SwitchActivity);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, value: "a", caseOutcome: null, ["actexec-switch", "actexec-a"]);
        await RunAsync(recorder, value: "no-match", caseOutcome: null, ["actexec-switch", "actexec-default"]);
        await RunAsync(recorder, value: "a", caseOutcome: "Break", ["actexec-switch", "actexec-a"]);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, string value, string? caseOutcome, string[] ids)
    {
        await using var harness = NewHarness(ids);
        var root = Nodes.Structural(
            "node-switch",
            typeof(SwitchActivity),
            SwitchActivity.StructureKind,
            SwitchActivity.StructureSchemaVersion,
            new
            {
                cases = new[] { new { match = "a", activity = "node-a" } },
                @default = "node-default"
            },
            [
                new ExecutableChildSlot(SwitchActivity.CaseSlotName("a"), [Probe("node-a", caseOutcome)]),
                new ExecutableChildSlot(SwitchActivity.DefaultSlotName, [Probe("node-default")])
            ],
            new Dictionary<string, object?> { ["Value"] = value });

        recorder.Record(ActivityType, await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root)), "node-switch");
    }
}

/// <summary>Drives ForEach over a real collection, and again with a body that breaks out early.</summary>
public sealed class ForEachDrive : ControlFlowDrive
{
    public override Type ActivityType => typeof(ForEachActivity);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, bodyOutcome: null, ["actexec-foreach", "actexec-body-0", "actexec-body-1"]);
        await RunAsync(recorder, bodyOutcome: "Break", ["actexec-foreach", "actexec-body-0"]);
        // An empty collection must short-circuit to Done without ever scheduling the body.
        await RunAsync(recorder, bodyOutcome: null, ["actexec-foreach"], items: []);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, string? bodyOutcome, string[] ids, string[]? items = null)
    {
        await using var harness = NewHarness(ids);
        var body = Probe("node-body", bodyOutcome);
        var root = Nodes.Structural(
            "node-foreach",
            typeof(ForEachActivity),
            ForEachActivity.StructureKind,
            ForEachActivity.StructureSchemaVersion,
            new { body = body.ExecutableNodeId },
            [new ExecutableChildSlot(ForEachActivity.BodySlotName, [body])],
            new Dictionary<string, object?> { ["Collection"] = items ?? ["first", "second"] });

        recorder.Record(ActivityType, await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root)), "node-foreach");
    }
}

/// <summary>Drives For over a real range, and again with a body that breaks; also pins the EndInclusive contract.</summary>
public sealed class ForDrive : ControlFlowDrive
{
    public override Type ActivityType => typeof(ForActivity);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        // Half-open [0, 2) → two passes. Elsa 3's inclusive default would have run three (#1117).
        var exclusive = await RunAsync(recorder, endInclusive: false, bodyOutcome: null, ["actexec-for", "actexec-b0", "actexec-b1"]);
        Xunit.Assert.Equal(2, exclusive.States("node-body").Count);

        var inclusive = await RunAsync(recorder, endInclusive: true, bodyOutcome: null, ["actexec-for", "actexec-b0", "actexec-b1", "actexec-b2"]);
        Xunit.Assert.Equal(3, inclusive.States("node-body").Count);

        await RunAsync(recorder, endInclusive: false, bodyOutcome: "Break", ["actexec-for", "actexec-b0"]);
    }

    private async Task<WorkflowExecutionRun> RunAsync(
        ActivityDriveRecorder recorder,
        bool endInclusive,
        string? bodyOutcome,
        string[] ids)
    {
        await using var harness = NewHarness(ids);
        var body = Probe("node-body", bodyOutcome);
        var root = Nodes.Structural(
            "node-for",
            typeof(ForActivity),
            ForActivity.StructureKind,
            ForActivity.StructureSchemaVersion,
            new { body = body.ExecutableNodeId },
            [new ExecutableChildSlot(ForActivity.BodySlotName, [body])],
            new Dictionary<string, object?>
            {
                ["Start"] = 0,
                ["End"] = 2,
                ["Step"] = 1,
                ["EndInclusive"] = endInclusive
            });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));
        recorder.Record(ActivityType, run, "node-for");
        return run;
    }
}

/// <summary>Drives While with a false condition (zero passes) and with a body that breaks out.</summary>
public sealed class WhileDrive : ControlFlowDrive
{
    public override Type ActivityType => typeof(WhileActivity);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await RunAsync(recorder, condition: false, bodyOutcome: null, ["actexec-while"]);
        await RunAsync(recorder, condition: true, bodyOutcome: "Break", ["actexec-while", "actexec-body"]);
    }

    private async Task RunAsync(ActivityDriveRecorder recorder, bool condition, string? bodyOutcome, string[] ids)
    {
        await using var harness = NewHarness(ids);
        var body = Probe("node-body", bodyOutcome);
        var root = Nodes.Structural(
            "node-while",
            typeof(WhileActivity),
            WhileActivity.StructureKind,
            WhileActivity.StructureSchemaVersion,
            new { body = body.ExecutableNodeId },
            [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            new Dictionary<string, object?> { ["Condition"] = condition });

        recorder.Record(ActivityType, await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root)), "node-while");
    }
}

/// <summary>Drives Do: the body always runs once, then the condition decides whether to repeat.</summary>
public sealed class DoDrive : ControlFlowDrive
{
    public override Type ActivityType => typeof(DoActivity);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        // Condition false: exactly one pass, proving do-while's run-then-test order.
        var once = await RunAsync(recorder, condition: false, bodyOutcome: null, ["actexec-do", "actexec-body"]);
        Xunit.Assert.Single(once.States("node-body"));

        await RunAsync(recorder, condition: true, bodyOutcome: "Break", ["actexec-do", "actexec-body"]);
    }

    private async Task<WorkflowExecutionRun> RunAsync(
        ActivityDriveRecorder recorder,
        bool condition,
        string? bodyOutcome,
        string[] ids)
    {
        await using var harness = NewHarness(ids);
        var body = Probe("node-body", bodyOutcome);
        var root = Nodes.Structural(
            "node-do",
            typeof(DoActivity),
            DoActivity.StructureKind,
            DoActivity.StructureSchemaVersion,
            new { body = body.ExecutableNodeId },
            [new ExecutableChildSlot(DoActivity.BodySlotName, [body])],
            new Dictionary<string, object?> { ["Condition"] = condition });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));
        recorder.Record(ActivityType, run, "node-do");
        return run;
    }
}

/// <summary>Drives a Parallel fork: both branches run and the join completes the composite.</summary>
public sealed class ParallelDrive : ControlFlowDrive
{
    public override Type ActivityType => typeof(ParallelActivity);

    public override async Task DriveAsync(ActivityDriveRecorder recorder)
    {
        await using var harness = NewHarness("actexec-parallel", "actexec-left", "actexec-right");
        var left = Probe("node-left");
        var right = Probe("node-right");
        var root = Nodes.Structural(
            "node-parallel",
            typeof(ParallelActivity),
            ParallelActivity.StructureKind,
            ParallelActivity.StructureSchemaVersion,
            new
            {
                branches = new[]
                {
                    new { name = "left", activity = left.ExecutableNodeId },
                    new { name = "right", activity = right.ExecutableNodeId }
                }
            },
            [
                new ExecutableChildSlot(ParallelActivity.BranchSlotName("left"), [left]),
                new ExecutableChildSlot(ParallelActivity.BranchSlotName("right"), [right])
            ]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root));
        recorder.Record(ActivityType, run, "node-parallel");
        run.AssertRan("node-left", "node-right");
    }
}
