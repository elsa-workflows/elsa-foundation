using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.ForEach;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
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
[Collection("ConsoleCapture")]
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
    public async Task BodyWriteLine_PrintsBoundTextForEachIteration()
    {
        await using var harness = NewHarnessWithPrimitives("actexec-foreach", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var (run, output) = await CaptureConsoleAsync(() => harness.RunAsync(NewExecutableWithWriteLineBody(["1", "2", "3"], "Item X")));

        Assert.Equal(new[] { "Item X", "Item X", "Item X" }, ConsoleLines(output));
        Assert.Equal(3, run.States(BodyNodeId).Count);
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

    [Fact]
    public async Task BodyBreak_EndsLoopEarly_AfterTheFirstItem()
    {
        // Collection has three items, but the body breaks on the first: exactly one body pass (< 3) runs
        // and the loop completes without advancing to the remaining items.
        await using var harness = NewHarness("actexec-foreach", "actexec-body-0");

        var run = await harness.RunAsync(NewExecutable(["a", "b", "c"], breakOnEntry: true));

        Assert.Single(run.States(BodyNodeId));
        run.AssertOutcomes(ForEachNodeId, ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<ForEachActivityConstructor>()
            .WithProbeLeaf()
            .Build(activityExecutionIds);

    private static WorkflowExecutionHarness NewHarnessWithPrimitives(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithConstructor<ForEachActivityConstructor>()
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string>? collection, bool breakOnEntry = false)
    {
        // A body that completes with the Break outcome models a Break leaf placed in the loop body (#299);
        // the loop recognizes it by name and ends early instead of advancing to the next item.
        var bodyOutcomes = breakOnEntry ? new[] { ActivityOutcomes.Break } : null;
        var root = new ExecutableNode(
            executableNodeId: ForEachNodeId,
            authoredActivityId: "authored-foreach",
            activityType: typeof(ForEachActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(ForEachActivityConstructor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new ForEachDescriptor())),
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
            childSlots: [new ExecutableChildSlot(ForEachActivity.BodySlotName, [WorkflowExecutionHarness.NewProbeNode(BodyNodeId, bodyOutcomes)])],
            structure: new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = BodyNodeId })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static WorkflowExecutable NewExecutableWithWriteLineBody(IReadOnlyCollection<string>? collection, string text)
    {
        var root = new ExecutableNode(
            executableNodeId: ForEachNodeId,
            authoredActivityId: "authored-foreach",
            activityType: typeof(ForEachActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(ForEachActivityConstructor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new ForEachDescriptor())),
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
            childSlots: [new ExecutableChildSlot(ForEachActivity.BodySlotName, [NewWriteLineNode(BodyNodeId, text)])],
            structure: new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = BodyNodeId })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static ExecutableNode NewWriteLineNode(string nodeId, string text) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(WriteLine).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(typeof(ClrActivityDescriptor).FullName!, RuntimeActivityDescriptor.InitialSchemaVersion, Serializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(WriteLine))))),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Text"] = new RuntimeInputBinding(
                    inputName: "Text",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonSerializer.SerializeToElement(text),
                    metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.String" })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static async Task<(WorkflowExecutionRun Run, string Output)> CaptureConsoleAsync(Func<Task<WorkflowExecutionRun>> action)
    {
        var original = Console.Out;
        await using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var run = await action();
            return (run, writer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static string[] ConsoleLines(string output) =>
        output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    private static IPayloadSerializer Serializer => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());

    private sealed class ForEachActivityConstructor : IActivityConstructor<ForEachDescriptor>
    {
        public static string ConsumerKeyValue => typeof(ForEachDescriptor).FullName!;
        public string ConsumerKey => ConsumerKeyValue;

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
