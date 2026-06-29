using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// In-process execution coverage for the code &amp; I/O leaf activities (<c>WriteLines</c>, <c>ReadLine</c>,
/// <c>Inline</c>) running through the real workflow agent via the shared <see cref="WorkflowExecutionHarness"/>
/// (#258). Each activity is constructed by the production <see cref="ClrActivityConstructor"/> from its
/// <see cref="TypeInformation"/> descriptor, exercising the full descriptor → construct → bind → execute
/// path. Asserts the leaf runs to completion, emits the default <c>Done</c> outcome, and that the run
/// completes.
/// </summary>
// WriteLines writes to Console.Out during the run; share the capture collection so output does not
// interleave with the other Console.Out-capturing tests.
[Collection("ConsoleCapture")]
public sealed class CodeIoLeafRuntimeTests
{
    [Fact]
    public async Task WriteLines_RunsToCompletion_AndEmitsDoneOutcome()
    {
        await using var harness = NewHarness("actexec-1");

        var node = NewClrLeafNode(
            "node-writelines",
            typeof(WriteLines),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Lines"] = LiteralBinding("Lines", new[] { "a", "b" }, "System.Collections.Generic.ICollection`1[[System.String]]")
            });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node));

        run.AssertOutcomes("node-writelines", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task ReadLine_RunsToCompletion_AndEmitsDoneOutcome()
    {
        await using var harness = NewHarness("actexec-1");

        var node = NewClrLeafNode("node-readline", typeof(ReadLine), inputBindings: new Dictionary<string, RuntimeInputBinding>());

        var original = Console.In;
        Console.SetIn(new StringReader("server-side line"));
        WorkflowExecutionRun run;
        try
        {
            run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node));
        }
        finally
        {
            Console.SetIn(original);
        }

        run.AssertOutcomes("node-readline", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Inline_RunsToCompletion_AndEmitsDoneOutcome()
    {
        await using var harness = NewHarness("actexec-1");

        var node = NewClrLeafNode(
            "node-inline",
            typeof(Inline),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Expression"] = LiteralBinding("Expression", "inline-value", "System.Object")
            });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node));

        run.AssertOutcomes("node-inline", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            // ClrActivityConstructor (registered by ActivitiesPrimitivesFeature) depends on IPayloadSerializer,
            // which the SerializationFeature provides; without it the activity factory cannot construct the leaf.
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .Build(activityExecutionIds);

    private static ExecutableNode NewClrLeafNode(string nodeId, Type activityType, IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(TypeInformation).FullName!,
            descriptorPayload: Serializer.SerializeToElement(TypeInformation.FromType(activityType)),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding LiteralBinding(string inputName, object value, string typeName) =>
        new(
            inputName: inputName,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeName });

    private static IPayloadSerializer Serializer => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
}
