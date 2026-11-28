using System.Text.Json;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Activation;
using Elsa.Activities.Graph.Runtime.Activities;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Graph.Runtime.Services;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed class GraphActivityExecutionTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ActivationStrategy_CreatesATransientActivityFromThePinnedGraphDescriptor()
    {
        await using var provider = CreateServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var descriptor = Descriptor();
        var runtimeDescriptor = RuntimeDescriptor(descriptor);
        var contract = RuntimeContract(runtimeDescriptor);
        var strategy = Assert.Single(
            scope.ServiceProvider.GetServices<IActivityActivationStrategy>(),
            item => item is GraphActivityActivationStrategy);

        await using var lease = await strategy.ActivateAsync(
            new ActivityActivationStrategyRequest(contract, runtimeDescriptor));
        var graph = Assert.IsType<GraphActivity>(lease.Activity);

        Assert.Equal("definition-version-1", graph.Descriptor.DefinitionVersionId);
        Assert.Equal("sha256:template-1", graph.Descriptor.TemplateHash);
    }

    [Fact]
    public void Descriptor_WithoutCompleteOutcomesDeserializesEmpty()
    {
        var json = JsonSerializer.SerializeToElement(Descriptor(), WebJson);
        var withoutField = JsonSerializer.Deserialize<JsonElement>(
            json.GetRawText().Replace(",\"completeOutcomes\":[]", "", StringComparison.Ordinal));

        var descriptor = withoutField.Deserialize<GraphActivityDescriptor>(WebJson)!;

        Assert.False(withoutField.TryGetProperty("completeOutcomes", out _));
        Assert.Empty(descriptor.CompleteOutcomes);
    }

    [Fact]
    public void Descriptor_RejectsDuplicateCompleteOutcomeReferenceKeys()
    {
        Assert.Throws<ArgumentException>(() => Descriptor(completeOutcomes: [new("fail", "Failed"), new("fail", "Other")]));
    }

    [Fact]
    public void ScopeFactory_FindsNearestScopeAndUsesCollisionSafeRoleKeys()
    {
        var factory = new GraphActivityScopeFactory(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
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
    public void RuntimeFeature_RegistersGraphRuntimeServices()
    {
        using var provider = CreateServiceProvider();

        var capability = Assert.Single(
            provider.GetServices<IRuntimeActivityConsumerCapability>(),
            item => item.ConsumerKey == WellKnownRuntimeActivityConsumers.GraphActivity);
        Assert.Equal([RuntimeActivityDescriptor.InitialSchemaVersion], capability.SupportedSchemaVersions);
        Assert.NotNull(provider.GetRequiredService<GraphActivityScopeFactory>());
        Assert.NotNull(provider.GetRequiredService<GraphActivityRecovery>());
        Assert.IsType<JsonRuntimeDurableValueStorageDriver>(
            provider.GetRequiredService<IRuntimeDurableValueStorageDriverRegistry>()
                .GetRequired(WellKnownRuntimeDurableValueStorageDrivers.Json));
    }

    private static RuntimeActivityDescriptor RuntimeDescriptor(GraphActivityDescriptor descriptor) =>
        new(
            WellKnownRuntimeActivityConsumers.GraphActivity,
            RuntimeActivityDescriptor.InitialSchemaVersion,
            JsonSerializer.SerializeToElement(descriptor, WebJson));

    private static ActivityContract RuntimeContract(RuntimeActivityDescriptor descriptor) =>
        new(
            "graph",
            "1.0.0",
            WellKnownRuntimeActivityConsumers.GraphActivity,
            descriptor.Payload,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(
                WellKnownRuntimeActivityConsumers.GraphActivity,
                WellKnownRuntimeActivityConsumers.GraphActivity));

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

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        WorkflowPublicationDefaults.Add(services);
        services.AddScoped<IDurableValueStateStore, UnusedValueStore>();
        new GraphActivitiesRuntimeHostingFeature().ConfigureServices(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static GraphActivityDescriptor Descriptor(IReadOnlyList<GraphActivityCompleteOutcomeDescriptor>? completeOutcomes = null) =>
        new(
            "definition-1",
            "definition-version-1",
            "1.0.0",
            "sha256:template-1",
            "entry",
            entryOccurrenceId: null,
            inputs: [],
            variables: [],
            outputs: [],
            outputMappings: [],
            requiredInputReferenceKeys: [],
            requiredOutputReferenceKeys: [],
            outcomeMappings: null,
            completeOutcomes: completeOutcomes);

    private sealed class UnusedValueStore : IDurableValueStateStore
    {
        public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Activating a graph must not write durable values.");

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Activating a graph must not delete durable values.");

        public ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Activating a graph must not read durable values.");

        public ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(
            DurableValueStatePageQuery query,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Activating a graph must not read durable values.");
    }
}
