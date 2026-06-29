using System.Text.Json;
using Elsa.Activities.For;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Tests;

/// <summary>
/// In-process execution coverage for the <c>For</c> counted-loop composite running through the real
/// workflow agent. Built on the shared <see cref="WorkflowExecutionHarness"/>: this file declares only the
/// For-specific activity constructor and graph shape; provider wiring, execution, and assertions come from
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

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesForFeature().ConfigureServices(services))
            .WithConstructor<ForActivityConstructor>()
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(int start, int end, int step, bool endInclusive = false, bool includeBody = true)
    {
        var childSlots = new List<ExecutableChildSlot>();
        if (includeBody)
            childSlots.Add(new ExecutableChildSlot(ForActivity.BodySlotName, [WorkflowExecutionHarness.NewProbeNode("node-body")]));

        var root = new ExecutableNode(
            executableNodeId: "node-for",
            authoredActivityId: "authored-for",
            activityType: typeof(ForActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ForActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Start"] = IntBinding("Start", start),
                ["End"] = IntBinding("End", end),
                ["Step"] = IntBinding("Step", step),
                ["EndInclusive"] = BoolBinding("EndInclusive", endInclusive)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
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
            inputName: name,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Int32" });

    private static RuntimeInputBinding BoolBinding(string name, bool value) =>
        new(
            inputName: name,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Boolean" });

    private sealed class ForActivityConstructor : IActivityConstructor<ForDescriptor>
    {
        public static string DescriptorTypeKey => typeof(ForDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(new ForDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            ForDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var activity = new ForActivity();
            if (inputs is not null)
            {
                if (inputs.TryGetValue("Start", out var start))
                    activity.Start = (InputArgument<int>)start;
                if (inputs.TryGetValue("End", out var end))
                    activity.End = (InputArgument<int>)end;
                if (inputs.TryGetValue("Step", out var step))
                    activity.Step = (InputArgument<int>)step;
                if (inputs.TryGetValue("EndInclusive", out var endInclusive))
                    activity.EndInclusive = (InputArgument<bool>)endInclusive;
            }

            return new(activity);
        }
    }

    private sealed record ForDescriptor;
}
