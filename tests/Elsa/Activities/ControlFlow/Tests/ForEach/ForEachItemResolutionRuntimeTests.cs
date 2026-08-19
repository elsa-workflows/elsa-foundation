using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.ForEach;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Xunit;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;

namespace Elsa.Activities.ForEach.Tests;

/// <summary>
/// End-to-end proof that a <c>ForEach</c> body resolves the current item and index through canonical
/// variable-read bindings and the live publish→run pipeline. Each pass receives a distinct typed
/// iteration frame owned by the ForEach node.
/// </summary>
public sealed class ForEachItemResolutionRuntimeTests
{
    private const string ForEachNodeId = "node-foreach";
    private const string BodyNodeId = "node-body";

    [Fact]
    public async Task Body_resolves_current_item_per_iteration_through_the_real_evaluator()
    {
        await using var harness = NewHarness("actexec-foreach", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(["alpha", "beta", "gamma"], ForEachActivity.CurrentItemVariableName));

        // Three body executions, one per item — assert each pass captured its own distinct item value
        // resolved through the real Variable-expression evaluator (in pass/iteration order).
        var captured = CapturedValuesInIterationOrder(run);
        Assert.Equal(["alpha", "beta", "gamma"], captured);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Body_resolves_zero_based_index_per_iteration_through_the_real_evaluator()
    {
        await using var harness = NewHarness("actexec-foreach", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(["x", "y", "z"], ForEachActivity.CurrentIndexVariableName));

        var captured = CapturedValuesInIterationOrder(run);
        Assert.Equal(["0", "1", "2"], captured);
        run.AssertWorkflowCompleted();
    }

    private static IReadOnlyList<string?> CapturedValuesInIterationOrder(WorkflowExecutionRun run) =>
        run.States(BodyNodeId)
            .Select(state => state.Completion!.Result.InlineValue!.Value.GetProperty("captured").GetString())
            .ToArray();

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowRuntimeFeature().ConfigureServices(services))
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string> collection, string bodyReferenceKey)
    {
        var capturesIndex = StringComparer.Ordinal.Equals(bodyReferenceKey, ForEachActivity.CurrentIndexVariableName);
        var targetAlias = capturesIndex ? "Int32" : "String";
        var captureActivityType = capturesIndex ? typeof(ForEachIndexCaptureActivity) : typeof(ForEachItemCaptureActivity);
        var body = new ExecutableNode(
            executableNodeId: BodyNodeId,
            authoredActivityId: "authored-body",
            activityType: captureActivityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test-legacy-descriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Value"] = new RuntimeInputBinding(
                    inputKey: "Value",
                    targetType: new ValueTypeDescriptor(targetAlias),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference(bodyReferenceKey, ForEachNodeId))
            },
            metadata: new Dictionary<string, string>());

        var root = new ExecutableNode(
            executableNodeId: ForEachNodeId,
            authoredActivityId: "authored-foreach",
            activityType: typeof(ForEachActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test-legacy-descriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Collection"] = CollectionBinding(collection)
            },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(ForEachActivity.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = BodyNodeId })));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static RuntimeInputBinding CollectionBinding(IReadOnlyCollection<string> collection)
    {
        var type = new ValueTypeDescriptor("Object");
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            nameof(ForEachActivity.Collection),
            type,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(collection), policy));
    }
}

public sealed class ForEachItemCaptureActivity : Activity<ForEachCaptureResult>
{
    [ActivityInput]
    public string Value { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ForEachCaptureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new ForEachCaptureResult(Value)));
}

public sealed class ForEachIndexCaptureActivity : Activity<ForEachCaptureResult>
{
    [ActivityInput]
    public int Value { get; set; }

    protected override ValueTask<ActivityTransition<ForEachCaptureResult>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new ForEachCaptureResult(
            Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
}

public sealed record ForEachCaptureResult([property: Output] string Captured);
