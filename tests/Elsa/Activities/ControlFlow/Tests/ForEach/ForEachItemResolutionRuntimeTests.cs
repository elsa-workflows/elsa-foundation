using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.ForEach;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
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
        var captured = CapturedValuesInIterationOrder(harness);
        Assert.Equal(["alpha", "beta", "gamma"], captured);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Body_resolves_zero_based_index_per_iteration_through_the_real_evaluator()
    {
        await using var harness = NewHarness("actexec-foreach", "actexec-body-0", "actexec-body-1", "actexec-body-2");

        var run = await harness.RunAsync(NewExecutable(["x", "y", "z"], ForEachActivity.CurrentIndexVariableName));

        var captured = CapturedValuesInIterationOrder(harness);
        Assert.Equal(["0", "1", "2"], captured);
        run.AssertWorkflowCompleted();
    }

    private static IReadOnlyList<string?> CapturedValuesInIterationOrder(WorkflowExecutionHarness harness)
    {
        var register = harness.Services.GetRequiredService<IRuntimeActivityOutputRegister>();
        // Body iteration executions are 'actexec-body-{index}'; read each pass's single captured output.
        var values = new List<string?>();
        for (var index = 0; ; index++)
        {
            var outputs = register.GetActivityOutputs(WorkflowExecutionHarness.WorkflowExecutionId, $"actexec-body-{index}");
            if (outputs.Count == 0)
                break;
            var captured = Assert.Single(outputs);
            values.Add(captured.Value.ValueKind == JsonValueKind.String ? captured.Value.GetString() : captured.Value.ToString());
        }

        return values;
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithConstructor<ForEachActivityConstructor>()
            .WithConstructor(new CaptureActivityConstructor())
            .ConfigureServices(services => services.AddSingleton<IWellKnownTypeRegistry>(
                ClrConstruction.RegistryFor(typeof(string), typeof(int))))
            .Build(activityExecutionIds);

    private static WorkflowExecutable NewExecutable(IReadOnlyCollection<string> collection, string bodyReferenceKey)
    {
        var capturesIndex = StringComparer.Ordinal.Equals(bodyReferenceKey, ForEachActivity.CurrentIndexVariableName);
        var targetAlias = capturesIndex ? "Int32" : "String";
        var targetClrType = capturesIndex ? "System.Int32" : "System.String";
        var body = new ExecutableNode(
            executableNodeId: BodyNodeId,
            authoredActivityId: "authored-body",
            activityType: "test/capture",
            activityTypeVersion: "1.0.0",
            descriptorType: CaptureActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new CaptureDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Value"] = new RuntimeInputBinding(
                    inputKey: "Value",
                    targetType: new ValueTypeDescriptor(targetAlias),
                    effectivePolicy: ValueProtectionPolicy.InstanceInline,
                    source: RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference(bodyReferenceKey, ForEachNodeId),
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = targetClrType,
                        ["referenceKey"] = "Value"
                    })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        var root = new ExecutableNode(
            executableNodeId: ForEachNodeId,
            authoredActivityId: "authored-foreach",
            activityType: typeof(ForEachActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: ForEachActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ForEachDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Collection"] = CollectionBinding(collection)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
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
        var type = new ValueTypeDescriptor("Elsa.Any", CollectionKind.List);
        var policy = ValueProtectionPolicy.InstanceInline;
        return new RuntimeInputBinding(
            nameof(ForEachActivity.Collection),
            type,
            policy,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(collection), policy),
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Object" });
    }

    private sealed class ForEachActivityConstructor : IActivityConstructor<ForEachDescriptor>
    {
        public static string DescriptorTypeKey => typeof(ForEachDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new ForEachDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(ForEachDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new ForEachActivity());
    }

    private sealed record ForEachDescriptor;

    private sealed class CaptureActivityConstructor : IActivityConstructor<CaptureDescriptor>
    {
        public static string DescriptorTypeKey => typeof(CaptureDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(NewCaptureActivity(inputs));

        public ValueTask<IActivity> Construct(CaptureDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(NewCaptureActivity(inputs));

        private static IActivity NewCaptureActivity(IDictionary<string, InputArgument>? inputs)
        {
            if (inputs is null || !inputs.TryGetValue("Value", out var input))
                return new CaptureActivity(null);
            return input switch
            {
                InputArgument<string> text => new CaptureActivity(text),
                InputArgument<int> number => new IntCaptureActivity(number),
                _ => throw new InvalidOperationException($"Unsupported capture input type '{input.GetType().Name}'.")
            };
        }
    }

    private sealed record CaptureDescriptor;

    // Reads its iteration-scoped "Value" input (resolved through the scope chain) and records it as output.
    private sealed class CaptureActivity(InputArgument<string>? value) : CodeActivity("test/capture")
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            var resolved = context.Get(value);
            context.Set(new OutputArgument<string>(new CapturedMemoryBlockReference()), resolved, "Captured");
            context.SetOutcomes([Workflows.Runtime.Core.Constants.ActivityOutcomes.Done]);
        }
    }

    private sealed class IntCaptureActivity(InputArgument<int> value) : CodeActivity("test/capture")
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            context.Set(
                new OutputArgument<string>(new CapturedMemoryBlockReference()),
                context.Get(value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Captured");
            context.SetOutcomes([Workflows.Runtime.Core.Constants.ActivityOutcomes.Done]);
        }
    }

    private sealed class CapturedMemoryBlockReference : IMemoryBlockReference
    {
        public string Id { get; set; } = "Captured";
        public IMemoryBlock Declare() => new CapturedMemoryBlock();
        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) => context.Get<T>(this);
        public T? Get<T>(IExpressionExecutionContext context) => context.Get<T>(this);
    }

    private sealed class CapturedMemoryBlock : IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }
}
