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

        // Each body pass resolved `index` through the canonical variable binding and returned it atomically.
        var values = run.States("node-body")
            .Select(state => state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetInt32())
            .ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, values);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task BodyResolvesCurrentIndex_ForNonUnitStep()
    {
        await using var harness = NewHarness("actexec-for", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(start: 2, end: 8, step: 2));

        var values = run.States("node-body")
            .Select(state => state.Completion!.Result.InlineValue!.Value.GetProperty("value").GetInt32())
            .ToArray();
        Assert.Equal(new[] { 2, 4, 6 }, values);
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services))
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
            descriptorType: typeof(ForDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Start"] = IntLiteral("Start", start),
                ["End"] = IntLiteral("End", end),
                ["Step"] = IntLiteral("Step", step)
            },
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
            activityType: typeof(IndexCaptureActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test-legacy-descriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                // Bind Value to the typed `index` variable declared by node-for.
                ["Value"] = new RuntimeInputBinding(
                    inputKey: "Value",
                    targetType: new ValueTypeDescriptor("Int32"),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference(ForActivity.IndexVariableName, "node-for"))
            },
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding IntLiteral(string name, int value) =>
        new(
            name,
            new ValueTypeDescriptor("Int32"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new ValueTypeDescriptor("Int32"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private sealed record ForDescriptor;
}
