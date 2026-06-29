using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Headline durability proof for <c>SetVariable</c> (#260) over the Seam-C keystone write-back (#286): a
/// <c>SetVariable</c> leaf mutates a <b>workflow-scope</b> variable, and a later sibling reads it through a real
/// JavaScript <c>variables.*</c> input expression and observes the value <c>SetVariable</c> wrote — proving the
/// mutation is persisted as a durable value and re-projected into materialization, not lost between activities.
///
/// The workflow declares <c>greeting</c> defaulting to <c>"unset"</c> on the Sequence root (its variables are
/// the workflow scope, the same shape Sequence/Flowchart emit). <c>SetVariable</c> sets it to <c>"set-value"</c>;
/// the downstream probe binds its input to JS <c>variables.greeting</c> and records what it resolves. Reading
/// <c>"set-value"</c> (not the <c>"unset"</c> default) proves the write-back carried the mutation forward.
/// </summary>
public sealed class SetVariableDurabilityExecutionTests
{
    private const string StringTypeName = "System.String";

    [Fact]
    public async Task SetVariable_MutationIsReadByDownstreamInputExpression_ThroughTheKeystoneWriteBack()
    {
        var probe = new VariableReadProbe();
        await using var harness = NewHarness(probe, "actexec-sequence", "actexec-set", "actexec-read");

        var run = await harness.RunAsync(NewExecutable());

        run.AssertWorkflowCompleted();
        // The downstream activity's `variables.greeting` input resolves to the value SetVariable wrote, not the
        // authored default — the keystone write-back persisted the mutation and re-projected it.
        Assert.Equal("set-value", probe.LastValue);
    }

    private static WorkflowExecutionHarness NewHarness(VariableReadProbe probe, params string[] activityExecutionIds)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            .WithConstructor<SequenceConstructor>()
            .WithConstructor(new SetVariableConstructor())
            .WithConstructor(new VariableReadProbeConstructor(probe))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddMemoryCache();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                new EventsFeature().ConfigureServices(services);
                new SerializationFeature().ConfigureServices(services);
                new ExpressionsFeature().ConfigureServices(services);
                new JavaScriptFeature().ConfigureServices(services);
                new JintFeature().ConfigureServices(services);
                // Surfaces variables/inputs/outputs to the engine at materialization time (self-contained).
                services.AddScoped<IScriptPreProcessor, MaterializationAccessorsPreProcessor>();
            })
            .Build(activityExecutionIds);

        RunStartupTasks(harness.Services);
        return harness;
    }

    private static void RunStartupTasks(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
            task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static WorkflowExecutable NewExecutable()
    {
        var setNode = new ExecutableNode(
            executableNodeId: "node-set",
            authoredActivityId: "authored-set",
            activityType: typeof(SetVariable).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: SetVariableDescriptor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new SetVariableDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Variable"] = StringLiteral("Variable", "greeting"),
                ["Value"] = ObjectLiteral("Value", "set-value")
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        var readNode = new ExecutableNode(
            executableNodeId: "node-read",
            authoredActivityId: "authored-read",
            activityType: "test/variable-read-probe",
            activityTypeVersion: "1.0.0",
            descriptorType: VariableReadProbeDescriptor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new VariableReadProbeDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                // Real JS expression over the workflow-scope variable: resolves the current projected value.
                ["Value"] = new(
                    inputName: "Value",
                    source: RuntimeInputBindingSource.Expression,
                    expression: new RuntimeExpressionBinding("JavaScript", "variables.greeting", new RuntimeValueTypeDescriptor("clr", StringTypeName, null)),
                    metadata: new Dictionary<string, string>
                    {
                        [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = StringTypeName,
                        ["referenceKey"] = "Value"
                    })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        var children = new[] { setNode, readNode };
        var structurePayload = new
        {
            activities = children.Select(child => child.ExecutableNodeId).ToArray(),
            variables = new[]
            {
                new VariableDefinition(
                    ReferenceKey: "var-greeting",
                    Name: "greeting",
                    TypeInformation: TypeInformation.String,
                    StorageDriverType: null,
                    Default: new ArgumentValue("unset", "Literal"))
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
        Literal(inputName, JsonSerializer.SerializeToElement(value), StringTypeName);

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

    private sealed record SetVariableDescriptor
    {
        public static string DescriptorTypeKey => typeof(SetVariableDescriptor).FullName!;
    }

    private sealed class SetVariableConstructor : IActivityConstructor<SetVariableDescriptor>
    {
        public string DescriptorType => SetVariableDescriptor.DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new SetVariableDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(SetVariableDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            var activity = new SetVariable();
            if (inputs is not null && inputs.TryGetValue("Variable", out var variableInput))
                activity.Variable = (InputArgument<string>)variableInput;
            if (inputs is not null && inputs.TryGetValue("Value", out var valueInput))
                activity.Value = (InputArgument<object>)valueInput;
            return new(activity);
        }
    }

    private sealed record VariableReadProbeDescriptor
    {
        public static string DescriptorTypeKey => typeof(VariableReadProbeDescriptor).FullName!;
    }

    private sealed class VariableReadProbe
    {
        public string? LastValue { get; private set; }
        public void Record(string? value) => LastValue = value;
    }

    private sealed class VariableReadProbeConstructor(VariableReadProbe probe) : IActivityConstructor<VariableReadProbeDescriptor>
    {
        public string DescriptorType => VariableReadProbeDescriptor.DescriptorTypeKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken) =>
            Construct(new VariableReadProbeDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(VariableReadProbeDescriptor descriptor, IDictionary<string, InputArgument>? inputs, IDictionary<string, OutputArgument>? outputs, CancellationToken cancellationToken)
        {
            if (inputs is null || !inputs.TryGetValue("Value", out var argument))
                throw new InvalidOperationException("VariableReadProbe expected a materialized 'Value' input argument.");

            return new(new VariableReadProbeActivity(probe, (InputArgument<string>)argument));
        }
    }

    private sealed class VariableReadProbeActivity(VariableReadProbe probe, InputArgument<string> value) : CodeActivity("test/variable-read-probe")
    {
        protected override void Execute(IActivityExecutionContext context) => probe.Record(context.Get(value));
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
