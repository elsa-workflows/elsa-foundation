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
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
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
        var materializer = new RuntimeActivityInputMaterializer();
        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "activity-1",
            durableValuesByValueId: new Dictionary<string, DurableValueState>(),
            activityOutputs: EmptyActivityOutputReader.Instance,
            serviceProvider: serviceProvider);

        var materialized = await materializer.MaterializeInputsAsync(node, resolutionContext);

        var textInput = Assert.Single(materialized);
        Assert.Equal("Text", textInput.Name);
        Assert.Equal("Hello World", textInput.Value);

        var writeLine = await ConstructWriteLineAsync(serviceProvider, textInput.Argument);
        var context = new SimpleActivityExecutionContext(serviceProvider, writeLine, CancellationToken.None);
        RuntimeActivityInputMemory.Seed(context, materialized);

        var output = await CaptureConsoleAsync(() => ((IActivity)writeLine).ExecuteAsync(context));

        Assert.Equal("Hello World", output.Trim());
    }

    private static async Task<WriteLine> ConstructWriteLineAsync(IServiceProvider serviceProvider, InputArgument textArgument)
    {
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var registry = new ActivityConstructorRegistry();
        registry.Add(ClrConstruction.Constructor(serviceProvider, serializer, typeof(WriteLine)));
        var factory = new ActivityFactory(registry);

        var activity = await factory.Create(
            ClrConstruction.DescriptorType,
            ClrConstruction.Payload(serializer, typeof(WriteLine)),
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
