using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Regression for #313: <see cref="Elsa.Activities.Primitives.Activities.SetVariable"/> exposes its value as
/// <c>InputArgument&lt;object&gt;</c>, but the runtime materializer builds the argument from the value's
/// <em>materialized</em> CLR type (<c>InputArgument&lt;int&gt;</c> for a literal <c>Int32</c>,
/// <c>InputArgument&lt;string&gt;</c> for a literal <c>String</c>). Because the closed generic is invariant, the
/// real <see cref="ClrActivityConstructor"/> + <c>ActivityArgumentBinder</c> path used to throw an
/// <c>InvalidOperationException</c> outside the materialization fault boundary — the run stalled at
/// <c>Running</c> silently. This test binds typed <c>Int32</c>/<c>String</c> values <b>directly</b> (NOT
/// pre-wrapped as <c>System.Object</c>) through the real <see cref="WorkflowExecutionHarness"/> and the real CLR
/// constructor, and asserts the variable is set and read back by a downstream activity.
/// </summary>
public sealed class SetVariableTypedBindingTests
{
    private const string IntTypeName = "System.Int32";
    private const string StringTypeName = "System.String";
    private const string ObjectTypeName = "System.Object";

    [Fact]
    public async Task SetVariable_WithTypedInt32AndStringValueBindings_SetsAndReadsThroughTheRealClrConstructor()
    {
        await using var harness = NewHarness(
            "actexec-sequence",
            "actexec-set-count",
            "actexec-set-label",
            "actexec-mirror");

        // (a) count = 42 bound as a literal Int32 (NOT System.Object) — the materializer yields InputArgument<int>.
        var setCount = SetVariableNode("node-set-count", "count", Literal(JsonSerializer.SerializeToElement(42), IntTypeName));

        // (b) label = "hi" bound as a literal String — the materializer yields InputArgument<string>.
        var setLabel = SetVariableNode("node-set-label", "label", Literal(JsonSerializer.SerializeToElement("hi"), StringTypeName));

        // (c) mirror = variables.count — a downstream activity reads the typed int the first node wrote.
        var mirror = SetVariableNode("node-mirror", "mirror", JavaScript("variables.count", IntTypeName));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSequence(
            children: [setCount, setLabel, mirror],
            variables:
            [
                new VariableDefinition("var-count", "count", new TypeReference("Int32"), null, new ArgumentValue(0, "Literal")),
                new VariableDefinition("var-label", "label", new TypeReference("String"), null, new ArgumentValue("none", "Literal")),
                new VariableDefinition("var-mirror", "mirror", new TypeReference("Int32"), null, new ArgumentValue(0, "Literal"))
            ])));

        // The whole run drains to Completed rather than stalling at Running (the #313 symptom).
        run.AssertWorkflowCompleted();

        // Every node ran, and the typed values flowed through and are observable downstream.
        var durableValues = await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Equal(42, VariableInt(durableValues, "count"));
        Assert.Equal("hi", VariableString(durableValues, "label"));
        Assert.Equal(42, VariableInt(durableValues, "mirror"));
    }

    private static WorkflowExecutionHarness NewHarness(params string[] activityExecutionIds)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services))
            // The real CLR constructor (#313's failing seam) and its ActivityArgumentBinder.
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithConstructor<SequenceConstructor>()
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
                services.AddScoped<IScriptPreProcessor, MaterializationAccessorsPreProcessor>();
            })
            .Build(activityExecutionIds);

        using (var scope = harness.Services.CreateScope())
            foreach (var task in scope.ServiceProvider.GetServices<IStartupTask>())
                task.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();

        return harness;
    }

    private static ExecutableNode NewSequence(IReadOnlyList<ExecutableNode> children, VariableDefinition[] variables)
    {
        var structurePayload = new
        {
            activities = children.Select(child => child.ExecutableNodeId).ToArray(),
            variables
        };

        return new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(SequenceConstructor.SequenceConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new SequenceDescriptor())),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
    }

    private static ExecutableNode SetVariableNode(string nodeId, string variableName, RuntimeInputBinding valueBinding) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(Elsa.Activities.Primitives.Activities.SetVariable).FullName!,
            activityTypeVersion: "1.0.0",
            // The real CLR descriptor — construction flows through ClrActivityConstructor + ActivityArgumentBinder.
            descriptor: new RuntimeActivityDescriptor(ClrConstruction.ConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(
                new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(typeof(Elsa.Activities.Primitives.Activities.SetVariable))))),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                // Variable name is itself a typed String binding; Value is the typed binding under test.
                ["Variable"] = Literal("Variable", JsonSerializer.SerializeToElement(variableName), StringTypeName),
                ["Value"] = valueBinding
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding Literal(JsonElement value, string typeName) => Literal("Value", value, typeName);

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

    private static RuntimeInputBinding JavaScript(string expression, string typeName) =>
        new(
            inputName: "Value",
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", expression, new RuntimeValueTypeDescriptor("clr", typeName, null)),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeName,
                ["referenceKey"] = "Value"
            });

    private static int VariableInt(IEnumerable<DurableValueState> durableValues, string variableName) =>
        LatestVariable(durableValues, variableName).InlineValue!.Value.GetInt32();

    private static string? VariableString(IEnumerable<DurableValueState> durableValues, string variableName) =>
        LatestVariable(durableValues, variableName).InlineValue!.Value.GetString();

    private static DurableValueState LatestVariable(IEnumerable<DurableValueState> durableValues, string variableName) =>
        durableValues
            .Where(durable => durable.Metadata.TryGetValue(RuntimeMetadataKeys.VariableName, out var name) && name == variableName)
            .OrderBy(durable => durable.CapturedAt)
            .Last();

    private sealed record SequenceDescriptor;

    private sealed class SequenceConstructor : IActivityConstructor<SequenceDescriptor>
    {
        public static string SequenceConsumerKey => typeof(SequenceDescriptor).FullName!;
        public string ConsumerKey => SequenceConsumerKey;

        public ValueTask<IActivity> Construct(JsonElement payload, IDictionary<string, Elsa.Activities.Runtime.Core.Models.InputArgument>? inputs, IDictionary<string, Elsa.Activities.Runtime.Core.Models.OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new SequenceActivity());

        public ValueTask<IActivity> Construct(SequenceDescriptor descriptor, IDictionary<string, Elsa.Activities.Runtime.Core.Models.InputArgument>? inputs, IDictionary<string, Elsa.Activities.Runtime.Core.Models.OutputArgument>? outputs, CancellationToken cancellationToken) =>
            new(new SequenceActivity());
    }
}
