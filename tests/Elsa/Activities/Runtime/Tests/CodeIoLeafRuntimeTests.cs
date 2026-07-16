using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Constants;
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
/// (#258). Migrated typed leaves carry a pinned activity contract and run through snapshot hydration plus
/// the transient CLR activator; legacy leaves retain the constructor adapter until their own migration slice.
/// Asserts each leaf runs to completion, emits the expected outcome, and that the run completes.
/// </summary>
// WriteLines writes to Console.Out during the run; share the capture collection so output does not
// interleave with the other Console.Out-capturing tests.
[Collection("ConsoleCapture")]
public sealed class CodeIoLeafRuntimeTests
{
    private static readonly ValueTypeDescriptor LinesType = new("String", CollectionKind.List);

    [Fact]
    public async Task WriteLines_RunsToCompletion_AndEmitsDoneOutcome()
    {
        await using var harness = NewHarness("actexec-1");

        var node = NewClrLeafNode(
            "node-writelines",
            typeof(WriteLines),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Lines"] = LiteralBinding("Lines", new List<string> { "a", "b" }, LinesType)
            });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node));

        var state = run.AssertOutcomes("node-writelines", ActivityOutcomes.Done);
        Assert.Equal(TypeAliasConvention.CanonicalAlias(typeof(ActivityUnit)), state.Completion?.Result.Type.Alias);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task WriteLines_ObjectExpressionLines_RunsToCompletion()
    {
        await using var harness = NewHarness("actexec-1");

        var node = NewClrLeafNode(
            "node-writelines",
            typeof(WriteLines),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Lines"] = ObjectExpressionBinding("Lines", new List<string> { "a", "b" }, LinesType)
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

        var state = run.AssertOutcomes("node-readline", ActivityOutcomes.Done);
        Assert.Equal("server-side line", state.Completion?.Result.InlineValue?.GetProperty("line").GetString());
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
                ["Expression"] = LiteralBinding("Expression", "inline-value", new ValueTypeDescriptor("Object"))
            });

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node));

        run.AssertOutcomes("node-inline", ActivityOutcomes.Done);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Break_RunsToCompletion_AndEmitsBreakOutcome()
    {
        // The Break leaf (#299) is constructed by the production ClrActivityConstructor and emits the
        // Break outcome (not the default Done); the enclosing loop reads that outcome to end early. Run on
        // its own here it completes the run, with Break recorded as its completion outcome.
        await using var harness = NewHarness("actexec-1");

        var node = NewClrLeafNode("node-break", typeof(Break), inputBindings: new Dictionary<string, RuntimeInputBinding>());

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(node));

        run.AssertOutcomes("node-break", ActivityOutcomes.Break);
        run.AssertWorkflowCompleted();
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            // Both the legacy constructor adapter and the typed CLR activator use the canonical payload serializer.
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .Build(activityExecutionIds);

    private static ExecutableNode NewClrLeafNode(string nodeId, Type activityType, IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(Serializer, activityType),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            activityContract: BuildTypedContract(activityType));

    private static ActivityContract? BuildTypedContract(Type activityType)
    {
        if (activityType == typeof(Break))
        {
            return NewContract(
                activityType,
                [],
                UnitResult(),
                [ActivityOutcomes.Break]);
        }

        if (activityType == typeof(Inline))
        {
            return NewContract(
                activityType,
                [new ActivityInputContract("Expression", nameof(Inline.Expression), new ValueTypeDescriptor("Object"), false, false, null, ActivityValuePolicy.Default)],
                new ActivityResultContract(new ValueTypeDescriptor("Object"), false, ActivityValuePolicy.Default, []));
        }

        if (activityType == typeof(WriteLines))
        {
            return NewContract(
                activityType,
                [new ActivityInputContract("Lines", nameof(WriteLines.Lines), LinesType, false, false, null, ActivityValuePolicy.Default)],
                UnitResult());
        }

        if (activityType == typeof(ReadLine))
        {
            return NewContract(
                activityType,
                [],
                new ActivityResultContract(
                    new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ReadLineResult))),
                    true,
                    ActivityValuePolicy.Default,
                    [new ActivityResultProjectionContract("Result", "line", new ValueTypeDescriptor("String"), false, ActivityValuePolicy.Default)]));
        }

        return null;
    }

    private static ActivityContract NewContract(
        Type activityType,
        IReadOnlyCollection<ActivityInputContract> inputs,
        ActivityResultContract result,
        IReadOnlyCollection<string>? outcomes = null) =>
        new(
            activityType.FullName!,
            "1.0.0",
            ClrConstruction.DescriptorType,
            ClrConstruction.Payload(Serializer, activityType),
            inputs,
            result,
            outcomes ?? [ActivityOutcomes.Done],
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, TypeAliasConvention.CanonicalAlias(activityType)));

    private static ActivityResultContract UnitResult() =>
        new(
            new ValueTypeDescriptor(TypeAliasConvention.CanonicalAlias(typeof(ActivityUnit))),
            true,
            ActivityValuePolicy.Default,
            []);

    private static RuntimeInputBinding LiteralBinding(string inputName, object value, ValueTypeDescriptor type) =>
        new(
            inputKey: inputName,
            targetType: type,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static RuntimeInputBinding ObjectExpressionBinding(string inputName, object value, ValueTypeDescriptor type) =>
        new(
            inputKey: inputName,
            targetType: type,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(WellKnownExpressionDescriptorTypes.Object, JsonSerializer.Serialize(value)));

    private static IPayloadSerializer Serializer => new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
}
