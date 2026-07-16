using System.Text.Json;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Activities;
using Elsa.Activities.Graph.Runtime.Constructors;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed class GraphActivityExecutionTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = CreateServiceProvider();

    [Fact]
    public async Task Constructor_UsesStableConsumerAndSchema()
    {
        var descriptor = Descriptor();
        var runtimeDescriptor = new RuntimeActivityDescriptor(
            WellKnownRuntimeActivityConsumers.GraphActivity,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            JsonSerializer.SerializeToElement(descriptor, WebJson));

        var activity = await new GraphActivityConstructor().ConstructAsync(
            runtimeDescriptor,
            new Dictionary<string, InputArgument>(),
            new Dictionary<string, OutputArgument>(),
            CancellationToken.None);

        var graph = Assert.IsType<GraphActivity>(activity);
        Assert.Equal("definition-version-1", graph.Descriptor.DefinitionVersionId);
        Assert.Equal("sha256:template-1", graph.Descriptor.TemplateHash);
    }

    [Fact]
    public async Task Execute_SchedulesOnlyTheDirectGraphRootInTheOuterExecutionScope()
    {
        var descriptor = Descriptor();
        var graph = NewGraph(descriptor);
        var nestedLeaf = Node("nested-leaf");
        var entry = Node("entry", [new ExecutableChildSlot("Children", [nestedLeaf])]);
        var boundary = BoundaryNode(descriptor, [entry]);
        var context = Context(graph, boundary, State("outer-execution"));

        await ((IActivity)graph).ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("entry", request.ExecutableNodeId);
        Assert.Equal("outer-execution", request.SchedulingActivityExecutionId);
        Assert.Equal("outer-execution", request.SchedulingProvenance.ParentActivityExecutionId);
        Assert.Equal("outer-execution", request.SchedulingProvenance.ExecutionScopeId);
        Assert.Equal("graph-entry", request.SchedulingProvenance.SchedulingCause);
        Assert.True(context.CompositeCompletionDeferred);
        Assert.False(context.CompositeCompletionRequested);

        await Assert.ThrowsAsync<InvalidOperationException>(() => graph.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "leaf-execution", "nested-leaf", [ActivityOutcomes.Done])).AsTask());
        Assert.False(context.CompositeCompletionRequested);

        await graph.OnChildCompletedAsync(
            new ActivityChildCompletedContext(context, "entry-execution", "entry", [ActivityOutcomes.Done]));
        Assert.False(context.CompositeCompletionRequested);

        var changes = await graph.PrepareCompletionCheckpointAsync(
            context,
            [],
            DateTimeOffset.UnixEpoch);

        Assert.Empty(changes);
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task Execute_RejectsAnEntryThatIsOnlyANestedDescendant()
    {
        var descriptor = Descriptor();
        var graph = NewGraph(descriptor);
        var boundary = BoundaryNode(descriptor,
        [
            Node("different-root", [new ExecutableChildSlot("Children", [Node("entry")])])
        ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IActivity)graph).ExecuteAsync(Context(graph, boundary, State("outer-execution"))).AsTask());

        Assert.Contains("direct child", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureInputs_DistinguishesExplicitNullAndMakesCapturedInputsReadOnly()
    {
        var scope = Scope();
        var descriptors = new[]
        {
            Input("explicit-null", Default("unused")),
            Input("defaulted", Default("fallback"))
        };
        var evaluations = 0;

        await scope.CaptureInputsAsync(
            descriptors,
            new Dictionary<string, object?> { ["explicit-null"] = null },
            new HashSet<string>(),
            (expression, _) =>
            {
                evaluations++;
                return ValueTask.FromResult<object?>(expression.Value.GetString());
            },
            DateTimeOffset.UnixEpoch);

        Assert.Equal(1, evaluations);
        Assert.Equal(JsonValueKind.Null, Assert.IsType<JsonElement>(await scope.GetRequiredInputAsync("explicit-null")).ValueKind);
        Assert.Equal("fallback", Assert.IsType<JsonElement>(await scope.GetRequiredInputAsync("defaulted")).GetString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.CaptureInputsAsync(
            descriptors,
            new Dictionary<string, object?>(),
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch).AsTask());
    }

    [Fact]
    public async Task CaptureInputs_UnknownDriverFailsBeforeEvaluationAndPublishesNothing()
    {
        var scope = Scope();
        var evaluations = 0;

        var exception = await Assert.ThrowsAsync<RuntimeDurableValueStorageDriverNotFoundException>(() => scope.CaptureInputsAsync(
            [Input("value", Default("fallback"), "unknown.driver")],
            new Dictionary<string, object?>(),
            new HashSet<string>(),
            (_, _) =>
            {
                evaluations++;
                return ValueTask.FromResult<object?>("value");
            },
            DateTimeOffset.UnixEpoch).AsTask());

        Assert.Contains("unknown.driver", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, evaluations);
        Assert.Empty(scope.Inputs);
    }

    [Fact]
    public async Task CaptureInputs_NonSerializableLateValuePublishesNoStagedInput()
    {
        var scope = Scope();
        var values = new Dictionary<string, object?>
        {
            ["serializable"] = "ready",
            ["opaque"] = new OpaqueValue(() => { })
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.CaptureInputsAsync(
            [Input("serializable"), Input("opaque")],
            values,
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch).AsTask());

        Assert.Empty(scope.Inputs);
    }

    [Fact]
    public async Task VariableAndOutputBatches_PublishNothingWhenLateEvaluationFails()
    {
        var variableScope = Scope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => variableScope.InitializeVariablesAsync(
            [Variable("first", Default("ok")), Variable("second", Default("fail"))],
            FailOnSentinel,
            DateTimeOffset.UnixEpoch).AsTask());
        Assert.Empty(variableScope.Variables);

        var outputScope = Scope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => outputScope.CaptureOutputsAsync(
            [Output("first"), Output("second")],
            [Mapping("first", Default("ok")), Mapping("second", Default("fail"))],
            new HashSet<string>(),
            FailOnSentinel,
            DateTimeOffset.UnixEpoch).AsTask());
        Assert.Empty(outputScope.Outputs);
    }

    [Fact]
    public async Task CaptureOutputs_EvaluatesBoundaryMappingOnceAndPublishesTheDurableResult()
    {
        var scope = Scope();
        var evaluations = 0;

        var captured = await scope.CaptureOutputsAsync(
            [Output("result")],
            [Mapping("result", Default("completed"))],
            new HashSet<string> { "result" },
            (expression, _) =>
            {
                evaluations++;
                return ValueTask.FromResult<object?>(expression.Value.GetString());
            },
            DateTimeOffset.UnixEpoch);

        var state = Assert.Single(captured);
        Assert.Equal(1, evaluations);
        Assert.Equal(GraphActivityScopeKeys.Output("outer-execution", "result"), state.DurableValueId);
        Assert.Equal("completed", state.InlineValue?.GetString());
        Assert.Same(state, scope.Outputs["result"]);
    }

    [Fact]
    public async Task ExternalDriver_RehydratesAndReadsANonSerializableInputAfterSuspension()
    {
        var externalDriver = new TestExternalStorageDriver();
        var registry = Registry(externalDriver);
        var factory = new GraphActivityScopeFactory(registry);
        var descriptor = Descriptor(inputs: [Input("opaque", storageDriverKey: externalDriver.DriverKey)]);
        var original = factory.Create(descriptor, "workflow-execution", "outer-execution");
        var opaque = new OpaqueValue(() => { });

        var persisted = await original.CaptureInputsAsync(
            descriptor.Inputs,
            new Dictionary<string, object?> { ["opaque"] = opaque },
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch);
        Assert.Equal(DurableValueStorage.External, Assert.Single(persisted).Storage);

        var rehydrated = factory.Rehydrate(
            descriptor,
            "workflow-execution",
            "outer-execution",
            persisted);

        Assert.Same(opaque, await rehydrated.GetRequiredInputAsync("opaque"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rehydrated.CaptureInputsAsync(
            descriptor.Inputs,
            new Dictionary<string, object?>(),
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch).AsTask());
    }

    [Fact]
    public async Task Rehydrate_RejectsPersistedStateWhoseCodecDoesNotMatchTheBoundaryDeclaration()
    {
        var externalDriver = new TestExternalStorageDriver();
        var factory = new GraphActivityScopeFactory(Registry(externalDriver));
        var descriptor = Descriptor(inputs: [Input("opaque", storageDriverKey: externalDriver.DriverKey)]);
        var original = factory.Create(descriptor, "workflow-execution", "outer-execution");
        var captured = Assert.Single(await original.CaptureInputsAsync(
            descriptor.Inputs,
            new Dictionary<string, object?> { ["opaque"] = new OpaqueValue(() => { }) },
            new HashSet<string>(),
            (_, _) => ValueTask.FromResult<object?>(null),
            DateTimeOffset.UnixEpoch));
        var tampered = new DurableValueState(
            captured.DurableValueId,
            captured.WorkflowExecutionId,
            captured.ValueId,
            new RuntimeValueTypeDescriptor(captured.Type.Kind, WellKnownRuntimeDurableValueStorageDrivers.Json, captured.Type.Schema),
            captured.Lifecycle,
            captured.Storage,
            captured.InlineValue,
            captured.ExternalReference,
            captured.SourceActivityExecutionId,
            captured.CapturedAt,
            captured.Metadata);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Rehydrate(
            descriptor,
            "workflow-execution",
            "outer-execution",
            [tampered]));

        Assert.Contains("declared type and storage driver", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopeFactory_FindsNearestScopeAndUsesCollisionSafeRoleKeys()
    {
        var factory = new GraphActivityScopeFactory(Registry());
        var descriptor = Descriptor();
        var outer = factory.Create(descriptor, "workflow-execution", "outer-execution");
        var nested = factory.Create(descriptor, "workflow-execution", "nested-execution");
        var descendant = State("descendant", "nested-execution");

        Assert.Same(nested, factory.FindNearest(descendant, [outer, nested]));
        Assert.NotEqual(
            GraphActivityScopeKeys.Input("ab", "c"),
            GraphActivityScopeKeys.Input("a", "bc"));
        Assert.NotEqual(
            GraphActivityScopeKeys.Input("outer-execution", "same"),
            GraphActivityScopeKeys.Variable("outer-execution", "same"));
    }

    [Fact]
    public void RuntimeFeature_RegistersConstructorAndScopeFactoryWithWorkflowRuntime()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<GraphActivityConstructor>(Assert.Single(provider.GetServices<IActivityConstructor>()));
        Assert.NotNull(provider.GetRequiredService<GraphActivityScopeFactory>());
        Assert.IsType<JsonRuntimeDurableValueStorageDriver>(
            provider.GetRequiredService<IRuntimeDurableValueStorageDriverRegistry>()
                .GetRequired(WellKnownRuntimeDurableValueStorageDrivers.Json));
    }

    public void Dispose() => _serviceProvider.Dispose();

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private GraphActivityScope Scope() =>
        new GraphActivityScopeFactory(Registry()).Create(Descriptor(), "workflow-execution", "outer-execution");

    private static IRuntimeDurableValueStorageDriverRegistry Registry(
        params IRuntimeDurableValueStorageDriver[] additionalDrivers) =>
        new RuntimeDurableValueStorageDriverRegistry(
            new IRuntimeDurableValueStorageDriver[] { new JsonRuntimeDurableValueStorageDriver() }.Concat(additionalDrivers));

    private SimpleActivityExecutionContext Context(
        GraphActivity graph,
        ExecutableNode node,
        ActivityExecutionState state) =>
        new(
            _serviceProvider,
            graph,
            CancellationToken.None,
            "workflow-execution",
            new WorkflowExecutableIdentity("artifact", "workflow", "workflow-version", "1", "sha256:workflow"),
            schedulerWorkItem: null,
            node,
            state);

    private static GraphActivity NewGraph(GraphActivityDescriptor descriptor) =>
        new(descriptor, new Dictionary<string, InputArgument>(), new Dictionary<string, OutputArgument>())
        {
            Id = "outer-execution",
            NodeId = "boundary"
        };

    private static ExecutableNode BoundaryNode(
        GraphActivityDescriptor descriptor,
        IReadOnlyCollection<ExecutableNode> children) =>
        new(
            "boundary",
            "authored-boundary",
            "graph",
            "1.0.0",
            RuntimeDescriptor(descriptor),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string> { ["graph.templateHash"] = descriptor.TemplateHash },
            [new ExecutableChildSlot("Graph.Entry", children)]);

    private static ExecutableNode Node(
        string nodeId,
        IReadOnlyCollection<ExecutableChildSlot>? slots = null) =>
        new(
            nodeId,
            $"authored-{nodeId}",
            "test/probe",
            "1.0.0",
            new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            slots);

    private static RuntimeActivityDescriptor RuntimeDescriptor(GraphActivityDescriptor descriptor) =>
        new(
            WellKnownRuntimeActivityConsumers.GraphActivity,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            JsonSerializer.SerializeToElement(descriptor, WebJson));

    private static ActivityExecutionState State(string activityExecutionId, string? executionScopeId = null) =>
        new(
            new ActivityExecution(activityExecutionId, "workflow-execution", "boundary", "authored-boundary", "graph", "1.0.0"),
            ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(
                "workflow-execution",
                null,
                null,
                null,
                null,
                null,
                executionScopeId,
                "test"),
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static GraphActivityDescriptor Descriptor(
        IReadOnlyList<GraphActivityInputDescriptor>? inputs = null) =>
        new(
            "definition-1",
            "definition-version-1",
            "1.0.0",
            "sha256:template-1",
            "entry",
            entryOccurrenceId: null,
            inputs ?? [],
            variables: [],
            outputs: [],
            outputMappings: [],
            requiredInputReferenceKeys: [],
            requiredOutputReferenceKeys: []);

    private static GraphActivityInputDescriptor Input(
        string referenceKey,
        GraphActivityExpression? @default = null,
        string storageDriverKey = WellKnownRuntimeDurableValueStorageDrivers.Json) =>
        new(referenceKey, Type(), storageDriverKey, @default);

    private static GraphActivityVariableDescriptor Variable(string referenceKey, GraphActivityExpression? initialValue) =>
        new(referenceKey, referenceKey, Type(), WellKnownRuntimeDurableValueStorageDrivers.Json, initialValue);

    private static GraphActivityOutputDescriptor Output(string referenceKey) =>
        new(referenceKey, Type(), WellKnownRuntimeDurableValueStorageDrivers.Json);

    private static GraphActivityOutputMappingDescriptor Mapping(string referenceKey, GraphActivityExpression source) =>
        new(referenceKey, source);

    private static GraphActivityExpression Default(string value) =>
        new("Literal", JsonSerializer.SerializeToElement(value));

    private static JsonElement Type() => JsonSerializer.SerializeToElement(new { alias = "object" });

    private static ValueTask<object?> FailOnSentinel(
        GraphActivityExpression expression,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return expression.Value.GetString() == "fail"
            ? ValueTask.FromException<object?>(new InvalidOperationException("late failure"))
            : ValueTask.FromResult<object?>(expression.Value.GetString());
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed record OpaqueValue(Action Callback);

    private sealed class TestExternalStorageDriver : IRuntimeDurableValueStorageDriver
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public string DriverKey => "test.external";

        public ValueTask<RuntimeDurableValueEncoding> EncodeAsync(
            object? value,
            RuntimeValueTypeDescriptor type,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = $"value-{_values.Count + 1}";
            _values.Add(locator, value);
            return ValueTask.FromResult(new RuntimeDurableValueEncoding(
                DurableValueStorage.External,
                null,
                new DurableValueExternalReference("test", locator, new Dictionary<string, string>())));
        }

        public ValueTask<object?> DecodeAsync(
            DurableValueState state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(state.Type.Id, DriverKey) ||
                state.Storage != DurableValueStorage.External ||
                state.ExternalReference is not { } reference)
            {
                throw new InvalidOperationException("Invalid external durable value state.");
            }

            return ValueTask.FromResult(_values[reference.Locator]);
        }
    }
}
