using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Activities;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using IfActivity = Elsa.Activities.If.Activities.If;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using SwitchActivity = Elsa.Activities.Switch.Activities.Switch;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Spec 237, User Story 1: a <c>Complete</c> node anywhere in a graph body ends the nearest graph boundary with its
/// outcome, through every composite in between, and the parent routes only that boundary outcome.
/// </summary>
public sealed class GraphBoundaryCompleteTests
{
    internal const string BoundaryNodeId = "node-boundary";
    private const string TemplateHash = "sha256:graph-template";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly GraphActivityCompleteOutcomeDescriptor[] FailOrSucceed =
    [
        new("fail", "Failed"),
        new("succeed", "Succeeded")
    ];

    [Fact]
    public async Task FlowchartBody_ExitRoutesOnlyTheChosenBoundaryOutcome()
    {
        var body = Nodes.Structural(
            "node-body",
            typeof(FlowchartActivity),
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure(
            [
                new FlowchartConnection(new FlowchartEndpoint("node-start", null), new FlowchartEndpoint("node-exit")),
                new FlowchartConnection(new FlowchartEndpoint("node-exit", null), new FlowchartEndpoint("node-after-exit"))
            ]),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [Probe("node-start"), CompleteNode("node-exit", "fail"), Probe("node-after-exit")])]);

        var run = await RunInParentAsync(Boundary(body, completeOutcomes: FailOrSucceed));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(BoundaryNodeId, "Failed");
        run.AssertRan("node-on-failed");
        run.AssertDidNotRun("node-on-succeeded", "node-after-exit");
        run.AssertCompleted("node-exit");
    }

    [Fact]
    public async Task FlowchartBody_NaturalCompletionRoutesTheMappedOutcome()
    {
        var body = Nodes.Structural(
            "node-body",
            typeof(FlowchartActivity),
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure([new FlowchartConnection(new FlowchartEndpoint("node-start", null), new FlowchartEndpoint("node-end"))]),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [Probe("node-start"), Probe("node-end")])]);

        var run = await RunInParentAsync(Boundary(body, outcomeMappings: [new(ActivityOutcomes.Done, "Succeeded")]));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(BoundaryNodeId, "Succeeded");
        run.AssertRan("node-on-succeeded");
    }

    [Fact]
    // #2756: the inner Flowchart's own path id used to leak to the outer Flowchart through command metadata.
    public async Task NestedFlowchart_InnerCompletionContinuesTheOuterFlowchart()
    {
        await using var harness = NewHarness();
        var inner = Nodes.Structural(
            "node-inner",
            typeof(FlowchartActivity),
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure([new FlowchartConnection(new FlowchartEndpoint("node-start", null), new FlowchartEndpoint("node-end"))]),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [Probe("node-start"), Probe("node-end")])]);
        var parent = Nodes.Structural(
            "node-parent",
            typeof(FlowchartActivity),
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure([new FlowchartConnection(new FlowchartEndpoint("node-inner", null), new FlowchartEndpoint("node-after"))]),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [inner, Probe("node-after")])]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(parent));

        run.AssertWorkflowCompleted();
        run.AssertRan("node-after");
    }

    [Fact]
    public async Task SequenceBody_LaterStepsNeverRun()
    {
        var body = Sequence("node-body", Probe("node-first"), CompleteNode("node-exit", "succeed"), Probe("node-third"));

        var run = await RunInParentAsync(Boundary(body, completeOutcomes: FailOrSucceed));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(BoundaryNodeId, "Succeeded");
        run.AssertRan("node-first", "node-on-succeeded");
        run.AssertDidNotRun("node-third", "node-on-failed");
    }

    [Fact]
    public async Task IfBody_ExitFromTheTakenBranch()
    {
        var body = Nodes.Structural(
            "node-body",
            typeof(IfActivity),
            IfActivity.StructureKind,
            IfActivity.StructureSchemaVersion,
            new { then = "node-exit", @else = "node-else" },
            [
                new ExecutableChildSlot(IfActivity.ThenSlotName, [CompleteNode("node-exit", "fail")]),
                new ExecutableChildSlot(IfActivity.ElseSlotName, [Probe("node-else")])
            ],
            new Dictionary<string, object?> { ["Condition"] = true });

        var run = await RunInParentAsync(Boundary(body, completeOutcomes: FailOrSucceed));

        run.AssertOutcomes(BoundaryNodeId, "Failed");
        run.AssertRan("node-on-failed");
    }

    [Fact]
    public async Task SwitchBody_ExitFromTheMatchedCase()
    {
        var body = Nodes.Structural(
            "node-body",
            typeof(SwitchActivity),
            SwitchActivity.StructureKind,
            SwitchActivity.StructureSchemaVersion,
            new { cases = new[] { new { match = "a", activity = "node-exit" } }, @default = "node-default" },
            [
                new ExecutableChildSlot(SwitchActivity.CaseSlotName("a"), [CompleteNode("node-exit", "succeed")]),
                new ExecutableChildSlot(SwitchActivity.DefaultSlotName, [Probe("node-default")])
            ],
            new Dictionary<string, object?> { ["Value"] = "a" });

        var run = await RunInParentAsync(Boundary(body, completeOutcomes: FailOrSucceed));

        run.AssertOutcomes(BoundaryNodeId, "Succeeded");
        run.AssertRan("node-on-succeeded");
    }

    [Fact]
    public async Task ForEachBody_TheLoopRunsNoFurtherIteration()
    {
        var loopBody = Sequence("node-loop-body", Probe("node-tick"), CompleteNode("node-exit", "fail"));
        var body = Nodes.Structural(
            "node-body",
            typeof(ForEachActivity),
            ForEachActivity.StructureKind,
            ForEachActivity.StructureSchemaVersion,
            new { body = loopBody.ExecutableNodeId },
            [new ExecutableChildSlot(ForEachActivity.BodySlotName, [loopBody])],
            new Dictionary<string, object?> { ["Collection"] = new[] { "first", "second", "third" } });

        var run = await RunInParentAsync(Boundary(body, completeOutcomes: FailOrSucceed));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(BoundaryNodeId, "Failed");
        Assert.Single(run.States("node-tick"));
    }

    [Fact]
    public async Task WhileBody_AnEndlessLoopEndsThroughTheExit()
    {
        var body = Nodes.Structural(
            "node-body",
            typeof(WhileActivity),
            WhileActivity.StructureKind,
            WhileActivity.StructureSchemaVersion,
            new { body = "node-exit" },
            [new ExecutableChildSlot(WhileActivity.BodySlotName, [CompleteNode("node-exit", "succeed")])],
            new Dictionary<string, object?> { ["Condition"] = true });

        var run = await RunInParentAsync(Boundary(body, completeOutcomes: FailOrSucceed));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(BoundaryNodeId, "Succeeded");
        Assert.Single(run.States("node-exit"));
    }

    [Fact]
    public async Task ParallelBody_TheBoundaryCompletesFromOneBranch()
    {
        var run = await RunInParentAsync(Boundary(
            Parallel("node-body", ("left", CompleteNode("node-exit", "fail")), ("right", Probe("node-right"))),
            completeOutcomes: FailOrSucceed));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(BoundaryNodeId, "Failed");
        run.AssertRan("node-on-failed");
    }

    [Fact]
    public async Task ParallelBody_TwoExitsTheFirstCommittedWins()
    {
        var run = await RunInParentAsync(Boundary(
            Parallel("node-body", ("left", CompleteNode("node-exit-left", "fail")), ("right", CompleteNode("node-exit-right", "succeed"))),
            completeOutcomes: FailOrSucceed));

        run.AssertWorkflowCompleted();
        var outcome = Assert.Single(WorkflowExecutionRun.CompletionOutcomes(run.State(BoundaryNodeId)));
        Assert.Contains(outcome, new[] { "Failed", "Succeeded" });
        Assert.Single(new[] { "node-on-failed", "node-on-succeeded" }, nodeId => run.States(nodeId).Count > 0);
    }

    [Fact]
    public async Task EntryIsComplete_TheBoundaryCompletesImmediately()
    {
        var run = await RunInParentAsync(Boundary(CompleteNode("node-exit", "succeed"), completeOutcomes: FailOrSucceed));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(BoundaryNodeId, "Succeeded");
        run.AssertCompleted("node-exit");
    }

    [Fact]
    public async Task NestedBoundary_OnlyTheNearestBoundaryCompletes()
    {
        var inner = Boundary(
            CompleteNode("node-exit", "fail"),
            completeOutcomes: [new("fail", "InnerFailed")],
            nodeId: "node-inner",
            definitionId: "definition-inner");
        var body = Sequence("node-body", inner, Probe("node-after-inner"));

        var run = await RunInParentAsync(Boundary(
            body,
            outcomeMappings: [new(ActivityOutcomes.Done, "Succeeded")],
            completeOutcomes: [new("fail", "Failed")]));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes("node-inner", "InnerFailed");
        run.AssertRan("node-after-inner", "node-on-succeeded");
        run.AssertOutcomes(BoundaryNodeId, "Succeeded");
    }

    [Fact]
    public async Task BodyThatNeverReachesComplete_UsesTheOutcomeMappingAsToday()
    {
        var body = Nodes.Structural(
            "node-body",
            typeof(IfActivity),
            IfActivity.StructureKind,
            IfActivity.StructureSchemaVersion,
            new { then = "node-exit", @else = "node-else" },
            [
                new ExecutableChildSlot(IfActivity.ThenSlotName, [CompleteNode("node-exit", "fail")]),
                new ExecutableChildSlot(IfActivity.ElseSlotName, [Probe("node-else")])
            ],
            new Dictionary<string, object?> { ["Condition"] = false });

        var run = await RunInParentAsync(Boundary(
            body,
            outcomeMappings: [new(ActivityOutcomes.False, "Succeeded")],
            completeOutcomes: [new("fail", "Failed")]));

        run.AssertOutcomes(BoundaryNodeId, "Succeeded");
        run.AssertDidNotRun("node-exit");
    }

    [Fact]
    public async Task BodyWithOnlyCompleteOutcomesThatEndsNaturally_FaultsTheBoundary()
    {
        var run = await RunInParentAsync(
            Boundary(Probe("node-plain-entry"), completeOutcomes: FailOrSucceed),
            allowPendingWork: true);

        var boundary = run.State(BoundaryNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, boundary.Status);
        Assert.Equal(GraphActivity.EntryCompletedWithoutMappingFaultCode, boundary.Fault!.Code);
    }

    [Fact]
    public async Task BreakInsideTheBody_StillEndsOnlyItsLoop()
    {
        var loop = Nodes.Structural(
            "node-loop",
            typeof(ForEachActivity),
            ForEachActivity.StructureKind,
            ForEachActivity.StructureSchemaVersion,
            new { body = "node-break" },
            [new ExecutableChildSlot(ForEachActivity.BodySlotName, [Probe("node-break", ActivityOutcomes.Break)])],
            new Dictionary<string, object?> { ["Collection"] = new[] { "first", "second" } });
        var body = Sequence("node-body", loop, CompleteNode("node-exit", "fail"));

        var run = await RunInParentAsync(Boundary(body, completeOutcomes: FailOrSucceed));

        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-break"));
        run.AssertCompleted("node-exit");
        run.AssertOutcomes(BoundaryNodeId, "Failed");
    }

    [Fact]
    public async Task OutputMappings_AreCapturedAtExit()
    {
        var run = await RunInParentAsync(Boundary(
            Sequence("node-body", CompleteNode("node-exit", "succeed"), Probe("node-never")),
            completeOutcomes: FailOrSucceed,
            outputs: [new GraphActivityOutputDescriptor("result", JsonSerializer.SerializeToElement(new { alias = "object" }), WellKnownRuntimeDurableValueStorageDrivers.Json)],
            outputMappings: [new GraphActivityOutputMappingDescriptor("result", new GraphActivityExpression("Literal", JsonSerializer.SerializeToElement("captured")))],
            requiredOutputReferenceKeys: ["result"]));

        run.AssertWorkflowCompleted();
        run.AssertOutcomes(BoundaryNodeId, "Succeeded");
        run.AssertDidNotRun("node-never");
    }

    [Fact]
    public async Task CompleteWithoutAnEnclosingBoundary_FaultsWithAStableCodeAndDoesNotFinishTheWorkflow()
    {
        await using var harness = NewHarness();
        var root = Sequence("node-root", CompleteNode("node-exit", "fail"), Probe("node-after"));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(root), allowPendingWorkOnTerminalCompletion: true);

        var exit = run.State("node-exit");
        Assert.Equal(ActivityExecutionStatus.Faulted, exit.Status);
        Assert.Equal("runtime.ancestor-notification.target-not-found", exit.Fault!.Code);
        run.AssertDidNotRun("node-after");
    }

    // The boundary sits in a parent Flowchart whose connections route each boundary outcome to its own probe, so a
    // test can see exactly which outcome the parent received.
    private static async Task<WorkflowExecutionRun> RunInParentAsync(ExecutableNode boundary, bool allowPendingWork = false)
    {
        await using var harness = NewHarness();
        var parent = Nodes.Structural(
            "node-parent",
            typeof(FlowchartActivity),
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure(
            [
                new FlowchartConnection(new FlowchartEndpoint(boundary.ExecutableNodeId, "Failed"), new FlowchartEndpoint("node-on-failed")),
                new FlowchartConnection(new FlowchartEndpoint(boundary.ExecutableNodeId, "Succeeded"), new FlowchartEndpoint("node-on-succeeded"))
            ]),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [boundary, Probe("node-on-failed"), Probe("node-on-succeeded")])]);

        return await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(parent), allowPendingWorkOnTerminalCompletion: allowPendingWork);
    }

    internal static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new GraphActivitiesRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));

    internal static ExecutableNode Probe(string nodeId, string? outcome = null) =>
        outcome is null
            ? WorkflowExecutionHarness.NewProbeNode(nodeId)
            : WorkflowExecutionHarness.NewProbeNode(nodeId, [outcome]);

    internal static ExecutableNode CompleteNode(string nodeId, string referenceKey) =>
        Nodes.Leaf(nodeId, typeof(Complete), new Dictionary<string, object?> { ["outcome"] = referenceKey });

    internal static ExecutableNode Sequence(string nodeId, params ExecutableNode[] steps) =>
        Nodes.Structural(
            nodeId,
            typeof(SequenceActivity),
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            new { activities = steps.Select(step => step.ExecutableNodeId).ToArray() },
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, steps)]);

    private static ExecutableNode Parallel(string nodeId, params (string Name, ExecutableNode Branch)[] branches) =>
        Nodes.Structural(
            nodeId,
            typeof(ParallelActivity),
            ParallelActivity.StructureKind,
            ParallelActivity.StructureSchemaVersion,
            new { branches = branches.Select(branch => new { name = branch.Name, activity = branch.Branch.ExecutableNodeId }).ToArray() },
            branches.Select(branch => new ExecutableChildSlot(ParallelActivity.BranchSlotName(branch.Name), [branch.Branch])).ToArray());

    // Mirrors what the publish compiler pins for a graph boundary (see GraphActivityDrive), extended with the
    // compiled Complete outcomes.
    internal static ExecutableNode Boundary(
        ExecutableNode entry,
        IReadOnlyList<GraphActivityOutcomeMappingDescriptor>? outcomeMappings = null,
        IReadOnlyList<GraphActivityCompleteOutcomeDescriptor>? completeOutcomes = null,
        IReadOnlyList<GraphActivityOutputDescriptor>? outputs = null,
        IReadOnlyList<GraphActivityOutputMappingDescriptor>? outputMappings = null,
        IReadOnlyList<string>? requiredOutputReferenceKeys = null,
        string nodeId = BoundaryNodeId,
        string definitionId = "definition-graph")
    {
        var descriptor = new GraphActivityDescriptor(
            definitionId: definitionId,
            definitionVersionId: $"{definitionId}-version",
            version: "1.0.0",
            templateHash: TemplateHash,
            entryNodeId: entry.ExecutableNodeId,
            entryOccurrenceId: null,
            inputs: [],
            variables: [],
            outputs: outputs ?? [],
            outputMappings: outputMappings ?? [],
            requiredInputReferenceKeys: [],
            requiredOutputReferenceKeys: requiredOutputReferenceKeys ?? [],
            outcomeMappings: outcomeMappings ?? [],
            completeOutcomes: completeOutcomes ?? []);
        var payload = JsonSerializer.SerializeToElement(descriptor, SerializerOptions);
        var outcomes = descriptor.OutcomeMappings.Select(mapping => mapping.BoundaryOutcomeName)
            .Concat(descriptor.CompleteOutcomes.Select(outcome => outcome.BoundaryOutcomeName))
            .Distinct(StringComparer.Ordinal)
            .DefaultIfEmpty(ActivityOutcomes.Done)
            .ToArray();

        var contract = new ActivityContract(
            "graph",
            "1.0.0",
            WellKnownRuntimeActivityConsumers.GraphActivity,
            payload,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                []),
            outcomes,
            new ActivityActivationRequirement(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                WellKnownRuntimeActivityConsumers.GraphActivity));

        return new ExecutableNode(
            nodeId,
            $"authored-{nodeId}",
            "graph",
            "1.0.0",
            new RuntimeActivityDescriptor(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                payload),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string> { ["graph.templateHash"] = TemplateHash },
            [new ExecutableChildSlot("Graph.Entry", [entry])],
            structure: null,
            activityContract: contract);
    }
}
