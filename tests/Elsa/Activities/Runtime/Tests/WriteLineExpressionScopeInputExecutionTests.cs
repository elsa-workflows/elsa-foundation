using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript.PreProcessors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// End-to-end guard for the workflow-scoped references that an activity input expression may use. A WriteLine
/// whose Text input is bound to a JavaScript expression must, at materialization time, resolve workflow
/// variables, workflow inputs, and prior activity outputs supplied by the
/// <see cref="RuntimeInputBindingResolutionContext"/> and print the computed value.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class WriteLineExpressionScopeInputExecutionTests
{
    private const string StringTypeName = "System.String";

    [Fact]
    public async Task ResolvesWorkflowVariableReference() =>
        await AssertResolvesAsync(
            "\"Hello, \" + variables.recipient + \"!\"",
            expected: "Hello, World!",
            variables: new Dictionary<string, object?> { ["recipient"] = "World" });

    [Fact]
    public async Task ResolvesWorkflowVariableViaGetVariableFunction() =>
        await AssertResolvesAsync(
            "\"Count is \" + getVariable(\"count\")",
            expected: "Count is 42",
            variables: new Dictionary<string, object?> { ["count"] = 42 });

    [Fact]
    public async Task ResolvesWorkflowInputReference() =>
        await AssertResolvesAsync(
            "getInput(\"firstName\") + \" \" + input.lastName",
            expected: "Ada Lovelace",
            inputs: new Dictionary<string, object?> { ["firstName"] = "Ada", ["lastName"] = "Lovelace" });

    [Fact]
    public async Task ResolvesPriorActivityOutputReference() =>
        await AssertResolvesAsync(
            "\"sum=\" + getOutput(\"Sum\") + \" product=\" + output.Product",
            expected: "sum=7 product=12",
            outputs: new Dictionary<string, object?> { ["Sum"] = 7, ["Product"] = 12 });

    [Fact]
    public async Task ResolvesAllScopesInASingleExpression() =>
        await AssertResolvesAsync(
            "variables.prefix + \": \" + input.subject + \" => \" + output.Result",
            expected: "greeting: world => 99",
            variables: new Dictionary<string, object?> { ["prefix"] = "greeting" },
            inputs: new Dictionary<string, object?> { ["subject"] = "world" },
            outputs: new Dictionary<string, object?> { ["Result"] = 99 });

    private static async Task AssertResolvesAsync(
        string expression,
        string expected,
        IReadOnlyDictionary<string, object?>? variables = null,
        IReadOnlyDictionary<string, object?>? inputs = null,
        IReadOnlyDictionary<string, object?>? outputs = null)
    {
        await using var provider = BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;

        var node = NewWriteLineNode("write-js", JavaScriptBinding(expression));
        var materializer = new RuntimeActivityInputMaterializer();
        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "activity-1",
            durableValuesByValueId: new Dictionary<string, DurableValueState>(),
            activityOutputs: EmptyActivityOutputReader.Instance,
            serviceProvider: serviceProvider,
            workflowVariables: variables,
            workflowInputs: inputs,
            activityOutputValues: outputs);

        var materialized = await materializer.MaterializeInputsAsync(node, resolutionContext);

        var textInput = Assert.Single(materialized);
        Assert.Equal("Text", textInput.Name);
        Assert.Equal(expected, textInput.Value);

        var writeLine = await ConstructWriteLineAsync(serviceProvider, textInput.Argument);
        var context = new SimpleActivityExecutionContext(serviceProvider, writeLine, CancellationToken.None);
        RuntimeActivityInputMemory.Seed(context, materialized);

        var output = await CaptureConsoleAsync(() => ((IActivity)writeLine).ExecuteAsync(context));

        Assert.Equal(expected, output.Trim());
    }

    private static async Task<WriteLine> ConstructWriteLineAsync(IServiceProvider serviceProvider, InputArgument textArgument)
    {
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var registry = new ActivityConstructorRegistry();
        registry.Add(ClrConstruction.Constructor(serviceProvider, serializer, typeof(WriteLine)));
        var factory = new ActivityFactory(registry);

        var activity = await factory.Create(
            ClrConstruction.Descriptor(serializer, typeof(WriteLine)),
            new Dictionary<string, InputArgument> { ["Text"] = textArgument },
            outputs: null);

        return Assert.IsType<WriteLine>(activity);
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

        // The materialization accessors pre-processor surfaces variables/inputs/outputs to the engine. It is
        // self-contained (no IWorkflowExecutionContext dependency), so it is registered directly rather than
        // via JavaScriptWorkflowsRuntimeFeature, whose other pre-processors require a live execution context.
        services.AddScoped<IScriptPreProcessor, MaterializationAccessorsPreProcessor>();

        return services.BuildServiceProvider();
    }

    private static ExecutableNode NewWriteLineNode(string nodeId, RuntimeInputBinding textBinding) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "Test.WriteLine",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(ClrConstruction.ConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, ClrConstruction.Payload(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()), typeof(WriteLine))),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { ["Text"] = textBinding },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static RuntimeInputBinding JavaScriptBinding(string expression) =>
        new(
            inputName: "Text",
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
}
