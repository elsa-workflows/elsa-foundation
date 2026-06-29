using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// In-process execution coverage for the data leaves <c>SetName</c>, <c>SetOutput</c>, and <c>SetVariables</c>
/// running through the real workflow agent on the shared <see cref="WorkflowExecutionHarness"/> (#260). Asserts
/// the engine drains each leaf intent and folds it into the activity-completed checkpoint, mirroring how the
/// <c>Correlate</c> execution test proves the correlation-id intent is persisted.
/// </summary>
/// <remarks>
/// As with the <c>Finish</c>/<c>Correlate</c> execution tests, each leaf is constructed by a focused test
/// <see cref="IActivityConstructor"/> rather than the production <c>ClrActivityConstructor</c>; the production
/// constructor is registered by <c>ActivitiesPrimitivesFeature</c> and exercised by the focused unit tests.
/// </remarks>
public sealed class SetDataLeafExecutionTests
{
    [Fact]
    public async Task SetName_SetsTheWorkflowInstanceName_OnTheWorkflowState()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithConstructor<SetNameConstructor>()
            .Build("actexec-set-name");

        var run = await harness.RunAsync(NewLeafExecutable("node-set-name", typeof(SetName), "Name", "order-flow"));

        run.AssertCompleted("node-set-name");
        run.AssertWorkflowCompleted();
        Assert.Equal("order-flow", run.WorkflowState?.SystemMetadata[RuntimeMetadataKeys.InstanceName]);
    }

    [Fact]
    public async Task SetOutput_PersistsAnOutputNameTaggedDurableValue()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithConstructor<SetOutputConstructor>()
            .Build("actexec-set-output");

        var run = await harness.RunAsync(NewSetOutputExecutable("node-set-output", "Result", "done"));

        run.AssertCompleted("node-set-output");
        run.AssertWorkflowCompleted();

        var durableValues = await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        var output = Assert.Single(durableValues, value => value.Metadata.ContainsKey(RuntimeMetadataKeys.OutputName) && value.Metadata[RuntimeMetadataKeys.OutputName] == "Result");
        Assert.Equal("done", output.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task SetVariables_SetsMultipleWorkflowScopeVariables_PersistedThroughTheWriteBack()
    {
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithConstructor<SequenceConstructor>()
            .WithConstructor<SetVariablesConstructor>()
            .Build("actexec-sequence", "actexec-set-variables");

        var run = await harness.RunAsync(NewSetVariablesExecutable());

        run.AssertWorkflowCompleted();

        // Both workflow-scope variables are re-emitted as VariableName-tagged durable values carrying the set
        // values — the keystone write-back persisted each mutation, mirroring SetVariable's single-variable path.
        var durableValues = await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal("alpha", VariableValue(durableValues, "first"));
        Assert.Equal("beta", VariableValue(durableValues, "second"));
    }

    private static string? VariableValue(IEnumerable<DurableValueState> durableValues, string variableName)
    {
        var value = durableValues
            .Where(durable => durable.Metadata.TryGetValue(RuntimeMetadataKeys.VariableName, out var name) && name == variableName)
            .OrderBy(durable => durable.CapturedAt)
            .Last();
        return value.InlineValue!.Value.GetString();
    }

    private static WorkflowExecutable NewLeafExecutable(string nodeId, Type activityType, string inputName, string inputValue)
    {
        var root = new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: TestDescriptor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new TestDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [inputName] = StringLiteral(inputName, inputValue)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static WorkflowExecutable NewSetOutputExecutable(string nodeId, string outputName, string outputValue)
    {
        var root = new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(SetOutput).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: TestDescriptor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new TestDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["OutputName"] = StringLiteral("OutputName", outputName),
                ["OutputValue"] = ObjectLiteral("OutputValue", outputValue)
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static WorkflowExecutable NewSetVariablesExecutable()
    {
        // Bind Variables as a literal JSON object typed as System.Object; the materializer seeds it as a
        // JsonElement under the memory id `node-set-variables:Variables`, which the SetVariables test
        // constructor reads back as an IDictionary<string, object?> (System.Text.Json maps the interface).
        var variablesPayload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["first"] = "alpha",
            ["second"] = "beta"
        };
        var setNode = new ExecutableNode(
            executableNodeId: "node-set-variables",
            authoredActivityId: "authored-set-variables",
            activityType: typeof(SetVariables).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: TestDescriptor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new TestDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Variables"] = new(
                    inputName: "Variables",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonSerializer.SerializeToElement(variablesPayload),
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Object",
                        ["referenceKey"] = "Variables"
                    })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        var children = new[] { setNode };
        var structurePayload = new
        {
            activities = children.Select(child => child.ExecutableNodeId).ToArray(),
            variables = new[]
            {
                new VariableDefinition("var-first", "first", TypeInformation.String, null, new ArgumentValue("unset", "Literal")),
                new VariableDefinition("var-second", "second", TypeInformation.String, null, new ArgumentValue("unset", "Literal"))
            }
        };

        var root = new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: SequenceConstructor.SequenceDescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static RuntimeInputBinding StringLiteral(string inputName, string value) =>
        Literal(inputName, JsonSerializer.SerializeToElement(value), "System.String");

    private static RuntimeInputBinding ObjectLiteral(string inputName, object? value) =>
        Literal(inputName, JsonSerializer.SerializeToElement(value), "System.Object");

    private static RuntimeInputBinding Literal(string inputName, JsonElement value, string typeName) =>
        new(
            inputName: inputName,
            source: RuntimeInputBindingSource.Literal,
            literalValue: value,
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeName,
                ["referenceKey"] = inputName
            });

    private sealed record TestDescriptor
    {
        public static string DescriptorTypeKey => typeof(TestDescriptor).FullName!;
    }

    private sealed class SetNameConstructor : IActivityConstructor<TestDescriptor>
    {
        public string DescriptorType => TestDescriptor.DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new TestDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(TestDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new SetName();
            if (inputs is not null && inputs.TryGetValue("Name", out var nameInput))
                activity.Name = (InputArgument<string>)nameInput;
            return new(activity);
        }
    }

    private sealed class SetOutputConstructor : IActivityConstructor<TestDescriptor>
    {
        public string DescriptorType => TestDescriptor.DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new TestDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(TestDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new SetOutput();
            if (inputs is not null && inputs.TryGetValue("OutputName", out var nameInput))
                activity.OutputName = (InputArgument<string>)nameInput;
            if (inputs is not null && inputs.TryGetValue("OutputValue", out var valueInput))
                activity.OutputValue = (InputArgument<object>)valueInput;
            return new(activity);
        }
    }

    private sealed class SetVariablesConstructor : IActivityConstructor<TestDescriptor>
    {
        public string DescriptorType => TestDescriptor.DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new TestDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(TestDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            // The materializer seeds the Variables literal (a JsonElement) under the materialized input's memory
            // id; re-bind it as InputArgument<IDictionary<string, object?>> over the same id so context.Get reads
            // the seeded value and deserializes it to the dictionary the activity iterates.
            if (inputs is null || !inputs.TryGetValue("Variables", out var materialized))
                throw new InvalidOperationException("SetVariables expected a materialized 'Variables' input argument.");

            var activity = new SetVariables
            {
                Variables = new InputArgument<IDictionary<string, object?>>(new SharedMemoryReference(materialized.MemoryBlockReference().Id))
            };
            return new(activity);
        }
    }

    // Shares the materialized input's memory-block id so the re-typed InputArgument reads the seeded value.
    private sealed class SharedMemoryReference(string id) : Elsa.Expressions.Core.Contracts.IMemoryBlockReference
    {
        public string Id { get; set; } = id;
        public Elsa.Expressions.Core.Contracts.IMemoryBlock Declare() => new SharedMemoryBlock();
        public T? Get<T>(Elsa.Expressions.Core.Contracts.IMemoryRegister memoryRegister, Elsa.Expressions.Core.Contracts.IExpressionExecutionContext context) => context.Get<T>(this);
        public T? Get<T>(Elsa.Expressions.Core.Contracts.IExpressionExecutionContext context) => context.Get<T>(this);
    }

    private sealed class SharedMemoryBlock : Elsa.Expressions.Core.Contracts.IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }

    private sealed record SequenceDescriptor;

    private sealed class SequenceConstructor : IActivityConstructor<SequenceDescriptor>
    {
        public static string SequenceDescriptorTypeKey => typeof(SequenceDescriptor).FullName!;
        public string DescriptorType => SequenceDescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new SequenceActivity());

        public ValueTask<IActivity> Construct(SequenceDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new SequenceActivity());
    }
}
