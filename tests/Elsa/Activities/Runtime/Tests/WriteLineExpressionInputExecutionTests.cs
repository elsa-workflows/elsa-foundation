using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// End-to-end guard for non-Literal activity input expressions: a WriteLine whose Text input is bound
/// to a JavaScript expression must evaluate that expression at materialization time and print the
/// computed value. Before the fix the compiler rejected non-Literal inputs and the materializer could
/// not evaluate expression bindings, so JavaScript/Liquid inputs never reached an activity.
/// </summary>
// Shares a collection with other Console.Out-capturing tests so xUnit does not run them in parallel
// (Console.SetOut is process-global; concurrent capture would interleave output).
[Collection("ConsoleCapture")]
public sealed class WriteLineExpressionInputExecutionTests
{
    private const string StringTypeName = "System.String";

    [Fact]
    public async Task WriteLine_PrintsEvaluatedJavaScript_WhenTextInputIsAnExpressionBinding()
    {
        await using var provider = BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;

        var node = NewWriteLineNode("write-js", JavaScriptBinding("\"Hello \" + \"World\""));
        var materializer = TypedActivityTestActivation.CreateMaterializer();
        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "activity-1",
            durableValuesByValueId: new Dictionary<string, DurableValueState>(),
            activityOutputs: EmptyActivityOutputReader.Instance,
            serviceProvider: serviceProvider);

        var materialized = await materializer.MaterializeInputsAsync(node, resolutionContext);

        var textInput = Assert.Single(materialized);
        Assert.Equal("text", textInput.Name);
        Assert.Equal("Hello World", textInput.Value);

        await using var activation = await TypedActivityTestActivation.ActivateAsync<WriteLine>(
            serviceProvider,
            new Dictionary<string, object?> { [textInput.Name] = textInput.Value });
        var writeLine = Assert.IsType<WriteLine>(activation.Activity);
        var context = new SimpleActivityExecutionContext(serviceProvider, writeLine, CancellationToken.None);

        var output = await CaptureConsoleAsync(async () => await ((IActivity)writeLine).ExecuteAsync(context));

        Assert.Equal("Hello World", output.Trim());
    }

    [Fact]
    public async Task Portable_expression_materializes_declared_parameters_into_one_restart_safe_pinned_snapshot()
    {
        var handler = new RecordingPortableHandler();
        var services = new ServiceCollection();
        new ExpressionsFeature().ConfigureServices(services);
        services.AddSingleton<IPortableExpressionHandler>(handler);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var binding = new RuntimeInputBinding(
            inputKey: "text",
            targetType: new ValueTypeDescriptor("String"),
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "Test",
                "args.message + args.suffix + args.marker",
                new RuntimeValueTypeDescriptor("alias", "String", null),
                parameters: new Dictionary<string, ExpressionParameterBinding>
                {
                    ["message"] = new WorkflowRequestExpressionParameterBinding("message"),
                    ["suffix"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement("!")),
                    ["marker"] = new VariableExpressionParameterBinding("workflow", "marker")
                },
                options: JsonSerializer.SerializeToElement(new { }),
                capabilityProfile: ExpressionCapabilityProfiles.BindingPureV1));
        var node = NewTypedWriteLineNode("write-portable", binding);
        var originalInputs = new Dictionary<string, ValueEnvelope>
        {
            ["message"] = ValueEnvelope.Inline(
                new ValueTypeDescriptor("String"),
                JsonSerializer.SerializeToElement("Hello"),
                ValueProtectionPolicy.InstanceInline)
        };
        var context = new RuntimeInputBindingResolutionContext(
            "wfexec-1",
            "activity-1",
            new Dictionary<string, DurableValueState>(),
            EmptyActivityOutputReader.Instance,
            scope.ServiceProvider,
            workflowInputEnvelopes: originalInputs,
            variableEnvelopes: new Dictionary<RuntimeVariableValueAddress, ValueEnvelope>
            {
                [new("workflow", "marker")] = ValueEnvelope.Inline(
                    new ValueTypeDescriptor("String"),
                    JsonSerializer.SerializeToElement("?"),
                    ValueProtectionPolicy.InstanceInline)
            });
        var materializer = TypedActivityTestActivation.CreateMaterializer();

        var snapshot = await materializer.MaterializeSnapshotAsync(
            node,
            "activity-1",
            context,
            new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));
        originalInputs["message"] = ValueEnvelope.Inline(
            new ValueTypeDescriptor("String"),
            JsonSerializer.SerializeToElement("Changed"),
            ValueProtectionPolicy.InstanceInline);
        var restartedSnapshot = JsonSerializer.Deserialize<ActivityInputSnapshot>(
            JsonSerializer.Serialize(snapshot))!;

        Assert.Equal("Hello!?", restartedSnapshot.Values["text"].InlineValue!.Value.GetString());
        Assert.Equal(snapshot.BindingFingerprint, restartedSnapshot.BindingFingerprint);
        Assert.Equal(["marker", "message", "suffix"], handler.Request!.ParameterValues.Keys);
        Assert.Equal("Hello", handler.Request.ParameterValues["message"].GetString());
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        new EventsFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static ExecutableNode NewWriteLineNode(string nodeId, RuntimeInputBinding textBinding) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "Test.WriteLine",
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()), typeof(WriteLine)),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["text"] = textBinding },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static ExecutableNode NewTypedWriteLineNode(string nodeId, RuntimeInputBinding textBinding)
    {
        var descriptor = ClrConstruction.Payload(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()), typeof(WriteLine));
        var contract = new ActivityContract(
            typeof(WriteLine).FullName!,
            "1.0.0",
            ClrConstruction.DescriptorType,
            descriptor,
            [new ActivityInputContract("text", nameof(WriteLine.Text), new ValueTypeDescriptor("String"), true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Activities.Runtime.Core.Models.ActivityUnit"), true, ActivityValuePolicy.Default, []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, TypeAliasConvention.CanonicalAlias(typeof(WriteLine))));
        return new ExecutableNode(
            nodeId,
            $"authored-{nodeId}",
            typeof(WriteLine).FullName!,
            "1.0.0",
            ClrConstruction.DescriptorType,
            descriptor,
            new Dictionary<string, RuntimeInputBinding> { ["text"] = textBinding },
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static RuntimeInputBinding JavaScriptBinding(string expression) =>
        new(
            inputKey: "text",
            targetType: new ValueTypeDescriptor("String"),
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", expression, new RuntimeValueTypeDescriptor("clr", StringTypeName, null)),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = StringTypeName,
                ["referenceKey"] = "Text"
            });

    private static async Task<string> CaptureConsoleAsync(Func<ValueTask> action)
    {
        var original = Console.Out;
        await using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return writer.ToString();
    }

    private sealed class EmptyActivityOutputReader : IRuntimeActivityOutputReader
    {
        public static readonly EmptyActivityOutputReader Instance = new();

        public bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output)
        {
            output = null!;
            return false;
        }

        public IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId) => [];
    }

    private sealed class RecordingPortableHandler : IPortableExpressionHandler
    {
        public string Language => "Test";
        public ExpressionEvaluationRequest? Request { get; private set; }

        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)
        {
            Request = request;
            var result = request.ParameterValues["message"].GetString() +
                         request.ParameterValues["suffix"].GetString() +
                         request.ParameterValues["marker"].GetString();
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }
}
