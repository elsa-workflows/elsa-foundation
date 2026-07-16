using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.For;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Tests;

/// <summary>
/// End-to-end proof that the loop body resolves the current index through a canonical variable-read
/// binding. The live harness activates a typed per-iteration frame and materializes the body's input
/// from that lexical frame before the body emits the resolved index as its outcome.
/// </summary>
public sealed class ForIndexResolutionEndToEndTests
{
    [Fact]
    public async Task BodyResolvesCurrentIndex_ThroughRealEvaluator_EachPass()
    {
        await using var harness = NewHarness("actexec-for", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(start: 0, end: 3, step: 1));

        // Each body pass resolved `index` through the real evaluator and emitted it as its outcome.
        var outcomes = run.States("node-body")
            .Select(state => WorkflowExecutionRun.CompletionOutcomes(state).Single())
            .ToArray();
        string[] expected = [$"{IndexCaptureActivity.OutcomePrefix}0", $"{IndexCaptureActivity.OutcomePrefix}1", $"{IndexCaptureActivity.OutcomePrefix}2"];
        Assert.Equal(expected, outcomes);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodyResolvesCurrentIndex_ForNonUnitStep()
    {
        await using var harness = NewHarness("actexec-for", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(start: 2, end: 8, step: 2));

        var outcomes = run.States("node-body")
            .Select(state => WorkflowExecutionRun.CompletionOutcomes(state).Single())
            .ToArray();
        string[] expected = [$"{IndexCaptureActivity.OutcomePrefix}2", $"{IndexCaptureActivity.OutcomePrefix}4", $"{IndexCaptureActivity.OutcomePrefix}6"];
        Assert.Equal(expected, outcomes);
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<ForActivityConstructor>()
            .WithConstructor<IndexCaptureActivityConstructor>()
            .ConfigureServices(services => services.AddSingleton<IWellKnownTypeRegistry>(
                ClrConstruction.RegistryFor(typeof(int))))
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(int start, int end, int step)
    {
        var body = NewIndexCaptureNode("node-body");
        var childSlots = new List<ExecutableChildSlot>
        {
            new(ForActivity.BodySlotName, [body])
        };

        var root = new ExecutableNode(
            executableNodeId: "node-for",
            authoredActivityId: "authored-for",
            activityType: typeof(ForActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ForActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Start"] = IntLiteral("Start", start),
                ["End"] = IntLiteral("End", end),
                ["Step"] = IntLiteral("Step", step)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                ForActivity.StructureKind,
                ForActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = "node-body" })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static ExecutableNode NewIndexCaptureNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: IndexCaptureActivity.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: IndexCaptureActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new IndexCaptureDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                // Bind Value to the typed `index` variable declared by node-for.
                ["Value"] = new RuntimeInputBinding(
                    inputKey: "Value",
                    targetType: new ValueTypeDescriptor("Int32"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference(ForActivity.IndexVariableName, "node-for"),
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Int32",
                        ["referenceKey"] = "Value"
                    })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding IntLiteral(string name, int value) =>
        new(
            inputName: name,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Int32" });

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
            => new(new ForActivity());
    }

    private sealed record ForDescriptor;
}
