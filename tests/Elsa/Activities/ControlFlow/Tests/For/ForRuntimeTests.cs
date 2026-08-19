using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.For;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Tests;

/// <summary>
/// In-process execution coverage for the <c>For</c> counted-loop composite running through the real
/// workflow agent. Built on the shared <see cref="WorkflowExecutionHarness"/>: this file declares only the
/// For-specific activity graph shape; provider wiring, CLR activation, execution, and assertions come from
/// the harness. Asserts the body runs the right count for a range, each pass sees the correct index, and an
/// empty range never runs the body yet still completes.
/// </summary>
public sealed class ForRuntimeTests
{
    [Fact]
    public async Task Range_RunsBodyOncePerIndex_AndCompletes()
    {
        // [0, 3) step 1 => 3 passes. One id for the For node, three for the body passes.
        await using var harness = NewHarness("actexec-for", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(start: 0, end: 3, step: 1));

        Assert.Equal(3, run.States("node-body").Count);
        run.AssertOutcomes("node-for", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task EachPass_CarriesItsIndexAsIterationId()
    {
        await using var harness = NewHarness("actexec-for", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(start: 0, end: 3, step: 1));

        var iterationIds = run.States("node-body").Select(state => state.IterationId).ToArray();
        Assert.Equal(new string?[] { "0", "1", "2" }, iterationIds);
    }

    [Fact]
    public async Task InclusiveEnd_RunsBodyThroughEndValue()
    {
        await using var harness = NewHarness("actexec-for", "actexec-body-0", "actexec-body-1", "actexec-body-2", "actexec-body-3");

        var run = await harness.RunAsync(NewExecutable(start: 0, end: 3, step: 1, endInclusive: true));

        Assert.Equal(new string?[] { "0", "1", "2", "3" }, run.States("node-body").Select(state => state.IterationId).ToArray());
    }

    [Fact]
    public async Task NonUnitStep_VisitsOnlySteppedIndices()
    {
        await using var harness = NewHarness("actexec-for", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(start: 1, end: 6, step: 2));

        Assert.Equal(new string?[] { "1", "3", "5" }, run.States("node-body").Select(state => state.IterationId).ToArray());
    }

    [Fact]
    public async Task DescendingRange_CountsDown()
    {
        await using var harness = NewHarness("actexec-for", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(start: 3, end: 0, step: -1));

        Assert.Equal(new string?[] { "3", "2", "1" }, run.States("node-body").Select(state => state.IterationId).ToArray());
    }

    [Fact]
    public async Task EmptyRange_NeverRunsBody_AndCompletes()
    {
        // [3, 3) is empty: the body must never run, yet the composite and workflow still complete.
        await using var harness = NewHarness("actexec-for");

        var run = await harness.RunAsync(NewExecutable(start: 3, end: 3, step: 1));

        run.AssertDidNotRun("node-body");
        run.AssertOutcomes("node-for", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task StepPointingAwayFromEnd_IsEmptyRange()
    {
        // Positive step with End < Start: empty range, no iterations, no infinite loop.
        await using var harness = NewHarness("actexec-for");

        var run = await harness.RunAsync(NewExecutable(start: 5, end: 0, step: 1));

        run.AssertDidNotRun("node-body");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task EmptyBody_CompletesWithoutRunning()
    {
        await using var harness = NewHarness("actexec-for");

        var run = await harness.RunAsync(NewExecutable(start: 0, end: 3, step: 1, includeBody: false));

        run.AssertOutcomes("node-for", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodyBreak_EndsLoopEarly_AfterASinglePass()
    {
        // Bound is 5 passes, but the body breaks on its first pass: exactly one body pass (< 5) runs and
        // the loop completes. Only one body id is needed because no further pass is scheduled.
        await using var harness = NewHarness("actexec-for", "actexec-body-0");

        var run = await harness.RunAsync(NewExecutable(start: 0, end: 5, step: 1, breakOnEntry: true));

        Assert.Single(run.States("node-body"));
        run.AssertOutcomes("node-for", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services))
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(int start, int end, int step, bool endInclusive = false, bool includeBody = true, bool breakOnEntry = false)
    {
        var childSlots = new List<ExecutableChildSlot>();
        if (includeBody)
        {
            // A body that completes with the Break outcome models a Break leaf placed in the loop body
            // (#299); the loop recognizes it by name and ends early.
            var bodyOutcomes = breakOnEntry ? new[] { ActivityOutcomes.Break } : null;
            childSlots.Add(new ExecutableChildSlot(ForActivity.BodySlotName, [WorkflowExecutionHarness.NewProbeNode("node-body", bodyOutcomes)]));
        }

        var root = new ExecutableNode(
            executableNodeId: "node-for",
            authoredActivityId: "authored-for",
            activityType: typeof(ForActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(ForDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Start"] = IntBinding("Start", start),
                ["End"] = IntBinding("End", end),
                ["Step"] = IntBinding("Step", step),
                ["EndInclusive"] = BoolBinding("EndInclusive", endInclusive)
            },
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                ForActivity.StructureKind,
                ForActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = includeBody ? "node-body" : null })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static RuntimeInputBinding IntBinding(string name, int value) =>
        new(
            name,
            new ValueTypeDescriptor("Int32"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Int32"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static RuntimeInputBinding BoolBinding(string name, bool value) =>
        new(
            name,
            new ValueTypeDescriptor("Boolean"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Boolean"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private sealed record ForDescriptor;
}
