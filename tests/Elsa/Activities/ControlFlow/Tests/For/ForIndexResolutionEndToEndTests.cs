using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.For;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions;
using Elsa.Expressions.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Tests;

/// <summary>
/// End-to-end proof that the loop body resolves the current index through the <b>real</b> expression
/// evaluator. The body's <c>Value</c> input is bound to a <c>Variable</c> expression referencing the
/// loop's per-iteration <c>index</c> variable (declaring scope = the For node). The body runs through the
/// real <see cref="WorkflowExecutionHarness"/> agent — which builds the per-iteration scope via
/// <see cref="RuntimeContainerScopeService"/> and materializes the input through the registered
/// <c>IExpressionEvaluator</c> — and emits the resolved value as its outcome. No stub/deterministic
/// evaluator is used: the recorded per-pass outcomes prove the index resolved in the body's scope chain.
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
            .WithFeature(services => new ExpressionsFeature().ConfigureServices(services))
            .WithConstructor<ForActivityConstructor>()
            .WithConstructor<IndexCaptureActivityConstructor>()
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
                // Bind Value to a Variable expression referencing the loop's `index` declared by node-for.
                ["Value"] = new RuntimeInputBinding(
                    inputName: "Value",
                    source: RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding(
                        WellKnownExpressionDescriptorTypes.Variable,
                        JsonSerializer.Serialize(new { referenceKey = ForActivity.IndexVariableName, declaringScopeId = "node-for" }),
                        new RuntimeValueTypeDescriptor("clr", "System.Int32", null)),
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
