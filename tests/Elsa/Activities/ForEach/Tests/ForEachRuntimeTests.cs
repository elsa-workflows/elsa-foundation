using System.Text.Json;
using Elsa.Activities.ForEach;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;

namespace Elsa.Activities.ForEach.Tests;

/// <summary>
/// In-process execution coverage for the <c>ForEach</c> composite running through the real workflow
/// agent. Built on the shared <see cref="WorkflowExecutionHarness"/>: the body is a probe leaf that
/// records one execution state per pass, so the test asserts the body runs once per item, that each pass
/// carries a distinct engine iteration id (ADR 0028), that completion drains, and that an empty/null
/// collection short-circuits without scheduling the body.
/// </summary>
public sealed class ForEachRuntimeTests
{
    private const string ForEachNodeId = "node-foreach";
    private const string BodyNodeId = "node-body";

    [Fact]
    public async Task BodyRunsOncePerItem_WithDistinctIterationIds()
    {
        await using var harness = NewHarness("actexec-foreach", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(["a", "b", "c"]));

        var bodyStates = run.States(BodyNodeId);
        Assert.Equal(3, bodyStates.Count);

        // Each pass carries a distinct iteration id of the form '{foreachExecId}:foreach-iteration:{index}'.
        var iterationIds = bodyStates.Select(state => state.IterationId).ToArray();
        Assert.Equal(
            new[] { "actexec-foreach:foreach-iteration:0", "actexec-foreach:foreach-iteration:1", "actexec-foreach:foreach-iteration:2" },
            iterationIds);
        Assert.Equal(iterationIds.Length, iterationIds.Distinct().Count());

        run.AssertOutcomes(ForEachNodeId, ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task SingleItem_RunsBodyOnce_AndCompletes()
    {
        await using var harness = NewHarness("actexec-foreach", "actexec-body-0");

        var run = await harness.RunAsync(NewExecutable(["only"]));

        var bodyState = Assert.Single(run.States(BodyNodeId));
        Assert.Equal("actexec-foreach:foreach-iteration:0", bodyState.IterationId);
        run.AssertOutcomes(ForEachNodeId, ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task EmptyCollection_ShortCircuits_WithoutRunningBody()
    {
        await using var harness = NewHarness("actexec-foreach");

        var run = await harness.RunAsync(NewExecutable([]));

        run.AssertDidNotRun(BodyNodeId);
        run.AssertOutcomes(ForEachNodeId, ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task NullCollection_ShortCircuits_WithoutRunningBody()
    {
        await using var harness = NewHarness("actexec-foreach");

        var run = await harness.RunAsync(NewExecutable(collection: null));

        run.AssertDidNotRun(BodyNodeId);
        run.AssertOutcomes(ForEachNodeId, ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesForEachFeature().ConfigureServices(services))
            .WithConstructor<ForEachActivityConstructor>()
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string>? collection)
    {
        var root = new ExecutableNode(
            executableNodeId: ForEachNodeId,
            authoredActivityId: "authored-foreach",
            activityType: typeof(ForEachActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ForEachActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForEachDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Collection"] = new RuntimeInputBinding(
                    inputName: "Collection",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonSerializer.SerializeToElement(collection),
                    metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Object" })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(ForEachActivity.BodySlotName, [WorkflowExecutionHarness.NewProbeNode(BodyNodeId)])],
            structure: new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = BodyNodeId })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private sealed class ForEachActivityConstructor : IActivityConstructor<ForEachDescriptor>
    {
        public static string DescriptorTypeKey => typeof(ForEachDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(new ForEachDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            ForEachDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var activity = new ForEachActivity();
            if (inputs is not null && inputs.TryGetValue("Collection", out var collectionInput))
                activity.Collection = (InputArgument<object>)collectionInput;
            return new(activity);
        }
    }

    private sealed record ForEachDescriptor;
}
