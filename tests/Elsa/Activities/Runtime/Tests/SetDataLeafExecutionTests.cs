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
/// In-process execution coverage for the data leaves <c>SetName</c> and <c>SetOutput</c>
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

        var run = await harness.RunAsync(NewLeafExecutable("node-set-name", typeof(SetName), "InstanceName", "order-flow"));

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
            if (inputs is not null && inputs.TryGetValue("InstanceName", out var nameInput))
                activity.InstanceName = (InputArgument<string>)nameInput;
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

}
