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
/// Highest-seam guard for Seam C (#254): workflow variables and inputs seeded as durable runtime state must
/// resolve through the projection + input-binding resolution context so that a JavaScript activity input
/// expression reading <c>variables.greeting</c> / <c>input.name</c> evaluates to the seeded value in
/// production — exercising seed → durable values → projection → resolution context → expression → activity.
/// </summary>
[Collection("ConsoleCapture")]
public sealed class WriteLineVariableInputExpressionExecutionTests
{
    private const string StringTypeName = "System.String";
    private readonly DateTimeOffset _now = new(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteLine_ResolvesSeededVariableAndInput_ThroughDurableRuntimeState()
    {
        await using var provider = BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;

        // Seed variables + inputs exactly as the runtime does at workflow start, persisting them as durable values.
        var store = new InMemoryDurableValueStateStore();
        var seedChanges = RuntimeWorkflowStateSeed.BuildSeedChanges(
            "wfexec-1",
            variables: new Dictionary<string, object?> { ["greeting"] = "Hello" },
            inputs: new Dictionary<string, object?> { ["name"] = "World" },
            capturedAt: _now);
        foreach (var change in seedChanges)
            await store.SaveAsync(change.State!);

        // Reload from the durable store (as a later activity / resumed instance would) and project into the context.
        var durableValues = await store.ListAsync("wfexec-1");
        var resolutionContext = new RuntimeInputBindingResolutionContext(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            durableValuesByValueId: durableValues.ToDictionary(value => value.ValueId, StringComparer.Ordinal),
            activityOutputs: EmptyActivityOutputReader.Instance,
            serviceProvider: serviceProvider,
            workflowVariables: RuntimeInputBindingStateProjection.ProjectWorkflowVariables(durableValues),
            workflowInputs: RuntimeInputBindingStateProjection.ProjectWorkflowInputs(durableValues),
            activityOutputValues: RuntimeInputBindingStateProjection.ProjectActivityOutputValues(durableValues));

        var node = NewWriteLineNode("write-js", JavaScriptBinding("variables.greeting + \" \" + input.name"));
        var materializer = new RuntimeActivityInputMaterializer();
        var materialized = await materializer.MaterializeInputsAsync(node, resolutionContext);

        var textInput = Assert.Single(materialized);
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
