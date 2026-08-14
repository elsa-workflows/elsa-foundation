using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// RT-4 guard: the runtime execution spine is composed by the host-agnostic <see cref="RuntimeCoreServiceCollectionExtensions.AddWorkflowRuntime"/>
/// composition root, so a non-HTTP host (worker, test harness, another module) can resolve and drive the runtime without
/// the FastEndpoints <c>WorkflowsRuntimeApiFeature</c>. Mirrors the failure class the review flagged: the runtime must not
/// be reachable only through the API feature.
/// </summary>
public sealed class RuntimeCoreCompositionRootTests : RuntimePipelineTestSupport
{
    private static readonly Type[] ScopedOperationServices =
    [
        typeof(IRuntimeActivityExecutionInspectionAccumulator),
        typeof(IBookmarkStimulusLookup),
        typeof(IBookmarkResumeDispatcher),
        typeof(IBookmarkConsumptionCheckpointService),
        typeof(IRuntimePauseDecisionProvider),
        typeof(IRuntimeRecoveryScanner),
        typeof(IRuntimeGeneratorEmissionScheduler),
        typeof(IWorkflowSchedulerPauseGate),
        typeof(IRuntimeExecutionOwnershipService),
        typeof(IRuntimeCheckpointCommitStore),
        typeof(IRuntimePostCommitOutboxStore),
        typeof(IPostCommitOutboxLookupStore),
        typeof(IRuntimePostCommitOutboxProcessor),
        typeof(IWorkflowOutputSource),
        typeof(IWorkflowDrainOrchestrator),
        typeof(WorkflowSchedulerCommandRouter),
        typeof(IRuntimeWorkflowExecutionPipeline),
        typeof(IRuntimeActivityExecutionPipeline),
        typeof(IRuntimeSchedulerPipelineSelector),
        typeof(IRuntimeExecutionPipelineDispatcher),
        typeof(IWorkflowSchedulerDrainer),
        typeof(IRuntimePostCommitIntentDispatcher),
        typeof(RuntimeCheckpointCommitter),
        typeof(IWorkflowStartDispatcher),
        typeof(IWorkflowExecutableRootWriteLeaseManager),
        typeof(IWorkflowExecutableReferenceGarbageCollector)
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddWorkflowRuntime_keeps_expression_consumers_inside_the_per_work_scope(bool registerExpressionsFirst)
    {
        var services = new ServiceCollection();
        if (registerExpressionsFirst)
            AddExpressionServices(services);
        services.AddWorkflowRuntime();
        if (!registerExpressionsFirst)
            AddExpressionServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerDrainer>());
        Assert.Contains(
            scope.ServiceProvider.GetServices<IWorkflowSchedulerWorkHandler>(),
            handler => handler is WorkflowStartActivitySchedulerWorkHandler);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRuntimeActivityInputMaterializer>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WorkflowIntrinsicExecutor>());
    }

    [Fact]
    public void AddWorkflowRuntime_registers_the_default_executable_input_validator()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(IWorkflowExecutableInputValidator));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<WorkflowExecutableInputValidator>(provider.GetRequiredService<IWorkflowExecutableInputValidator>());
    }

    [Fact]
    public void AddWorkflowRuntime_RegistersOneOverridableDefaultStartPolicy()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == typeof(IWorkflowExecutableStartPolicy));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<AllowWorkflowExecutableStartPolicy>(provider.GetRequiredService<IWorkflowExecutableStartPolicy>());
    }

    [Fact]
    public void AddWorkflowRuntime_HonorsPolicyRegisteredBeforeTheDefault()
    {
        var replacement = new ReplacementStartPolicy();
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowExecutableStartPolicy>(replacement);
        services.AddWorkflowRuntime();

        using var provider = services.BuildServiceProvider();

        Assert.Same(replacement, provider.GetRequiredService<IWorkflowExecutableStartPolicy>());
        Assert.NotNull(provider.GetRequiredService<IWorkflowStartDispatcher>());
    }

    [Fact]
    public void AddWorkflowRuntime_RejectsMultipleStartPolicyRegistrations()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddSingleton<IWorkflowExecutableStartPolicy, ReplacementStartPolicy>();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IWorkflowStartDispatcher>());

        Assert.Contains("exactly one", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(IWorkflowExecutableStartPolicy), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddWorkflowRuntime_SeparatesOperationScopesFromHostLifetimeState()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();

        Assert.All(ScopedOperationServices, serviceType =>
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == serviceType);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        });
        Assert.All(
            services.Where(candidate => candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler)),
            descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(services, candidate => candidate.ServiceType == typeof(IWorkflowExecutionCommandExecutor)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(services, candidate => candidate.ServiceType == typeof(IWorkflowExecutionActorProvider)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(services, candidate => candidate.ServiceType == typeof(InMemoryRuntimeCheckpointStoreState)).Lifetime);
        Assert.Equal(
            ServiceLifetime.Singleton,
            Assert.Single(services, candidate => candidate.ServiceType == typeof(IWorkflowExecutableStore)).Lifetime);
    }

    [Fact]
    public void AddWorkflowRuntime_ClaimsTheInMemoryTestScopeProviderIdempotently()
    {
        var services = new ServiceCollection();

        services.AddWorkflowRuntime();
        services.AddWorkflowRuntime();

        var registration = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(WorkflowTestScopeProviderRegistration));
        var claim = Assert.IsType<WorkflowTestScopeProviderRegistration>(registration.ImplementationInstance);
        Assert.Equal(typeof(InMemoryWorkflowTestScopeStore), claim.ProviderType);
        Assert.True(claim.IsInMemoryDefault);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeAdmissionStore));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestScopeCleanupStore));
    }

    [Fact]
    public void TestScopeProviderGuard_RejectsConflictingDurableProvidersInEitherOrder()
    {
        AssertConflict(typeof(FakeFirstTestScopeProvider), typeof(FakeSecondTestScopeProvider));
        AssertConflict(typeof(FakeSecondTestScopeProvider), typeof(FakeFirstTestScopeProvider));

        static void AssertConflict(Type first, Type second)
        {
            var services = new ServiceCollection();
            services.ClaimWorkflowTestScopeProvider(first);

            var exception = Assert.Throws<InvalidOperationException>(
                () => services.ClaimWorkflowTestScopeProvider(second));

            Assert.Contains(first.FullName!, exception.Message, StringComparison.Ordinal);
            Assert.Contains(second.FullName!, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AddWorkflowRuntime_ValidatesWithScopedPersistence()
    {
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddLogging();
        ReplaceWithScopedRuntimeStores(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<IWorkflowExecutionActorProvider>());
        Assert.NotNull(provider.GetRequiredService<IWorkflowExecutionCommandExecutor>());
    }

    [Fact]
    public async Task InProcessActor_UsesAndDisposesAFreshExecutionScopeForEachMailboxCommand()
    {
        var observations = new SchedulerQueueScopeObservations();
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddLogging();
        services.AddSingleton(observations);
        services.RemoveAll<IWorkflowSchedulerWorkQueue>();
        services.AddScoped<IWorkflowSchedulerWorkQueue, ScopeTrackingSchedulerWorkQueue>();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var actorProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var partition = new WorkflowExecutionPartition("tenant-blue");
        var actor = await actorProvider.GetAgentAsync(new WorkflowExecutionActorActivationRequest(
            "wfexec-scopes",
            WorkflowExecutionActorActivationReason.Start,
            Now,
            "scope-test",
            WorkflowExecutionActorCapabilities.InProcessMailbox,
            partition: partition));

        var first = await actor.EnqueueAsync(NewEnvelope(1, partition));
        var second = await actor.EnqueueAsync(NewEnvelope(2, partition));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, first.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, second.Status);
        Assert.Equal(2, observations.Created.Count);
        Assert.Equal(2, observations.Created.Distinct().Count());
        Assert.Equal(observations.Created.Order(), observations.Disposed.Order());
        Assert.Equal(["tenant-blue", "tenant-blue"], observations.Partitions);
        Assert.Same(actor, await actorProvider.GetAgentAsync(new WorkflowExecutionActorActivationRequest(
            "wfexec-scopes",
            WorkflowExecutionActorActivationReason.SchedulerWork,
            Now,
            "scope-test",
            WorkflowExecutionActorCapabilities.InProcessMailbox,
            partition: partition)));
    }

    [Fact]
    public void AddWorkflowRuntime_ResolvesTheExecutionSpine_WithoutTheApiFeature()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();

        // The whole dispatch graph must resolve from the Core composition root alone.
        Assert.NotNull(provider.GetService<IWorkflowSchedulerDrainer>());
        Assert.NotNull(provider.GetService<IWorkflowExecutionCommandExecutor>());
        Assert.NotNull(provider.GetService<IWorkflowDrainOrchestrator>());
        Assert.NotNull(provider.GetService<IRuntimeExecutionPipelineDispatcher>());
        Assert.NotNull(provider.GetService<IRuntimeWorkflowExecutionPipeline>());
        Assert.NotNull(provider.GetService<IRuntimeActivityExecutionPipeline>());
        Assert.NotNull(provider.GetService<IWorkflowExecutionActorProvider>());
        Assert.NotNull(provider.GetService<IWorkflowStartDispatcher>());
        Assert.NotEmpty(provider.GetServices<IWorkflowSchedulerWorkHandler>());
    }

    [Fact]
    public async Task AddWorkflowRuntime_DrivesADrainEndToEnd_WithoutTheApiFeature()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewExecutable());
        await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewActivityStateForStatus(ActivityExecutionStatus.Running));
        await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(NewCancelWorkItem());

        var result = await provider.GetRequiredService<IWorkflowSchedulerDrainer>()
            .DrainAsync(new RuntimeSchedulerDrainRequest("wf-1"));

        // The Cancel work item ran through the composed workflow pipeline (Invoke slot -> Checkpoint slot -> committer).
        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        var committed = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityCancelled, committed.Commit.Checkpoint.Name);
        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wf-1");
        Assert.Equal(WorkflowExecutionStatus.Cancelled, workflowState!.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddWorkflowRuntime_ParksAShedResumeOnlyWhenAResumptionRedriverIsComposed(bool composeResumption)
    {
        // #1320 through the container, not through a hand-built router. Every other test of this rule constructs
        // WorkflowSchedulerCommandRouter directly, so nothing pinned that the DI-composed one is handed the evidence
        // enumerable at all: rewriting its registration as an explicit factory omitting durabilityEvidence — the
        // prevailing style around it in RuntimeCoreServiceCollectionExtensions — would turn parking off on every
        // durable host while the suite stayed green. Driven both ways from one composition so the assertion is the
        // difference the evidence makes, not a property the container has either way.
        var services = new ServiceCollection();
        // Wins the TryAddSingleton in the composition root. StaticLimit clamps the controller's whole range to one
        // dispatch unit; on its own that still admits, because a lone command is never shed, so the held charge below
        // is the other half of forcing the refusal.
        services.AddSingleton(new RuntimeAdmissionOptions { StaticLimit = 1 });
        services.AddWorkflowRuntime();
        services.AddLogging();
        if (composeResumption)
            new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        // Opened on a separate async flow on purpose: a charge is ambient (AsyncLocal) and a command dispatched under
        // an ambient charge takes the nested-command exemption, so opening it inline would admit the very command
        // this test needs shed. The in-flight count it leaves behind is a plain field and outlives that flow.
        using var heldCharge = await Task.Run(provider.GetRequiredService<IRuntimeAdmissionLoadSignal>().OpenCharge);
        using var scope = provider.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<WorkflowSchedulerCommandRouter>();

        var result = await router.ProcessAsync(NewEnvelope(1, kind: WorkflowExecutionCommandKind.ResumeBookmark));

        Assert.True(result.Shed);
        Assert.False(result.DrainPerformed);
        Assert.Equal(composeResumption, result.ShedWorkQueued);
        var queued = await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAsync(new RuntimeSchedulerWorkQuery("wfexec-scopes"));
        Assert.Equal(composeResumption ? 1 : 0, queued.Items.Count);
    }

    private WorkflowExecutionCommandEnvelope NewEnvelope(
        int index,
        WorkflowExecutionPartition? partition = null,
        WorkflowExecutionCommandKind kind = WorkflowExecutionCommandKind.RunSchedulerWork)
    {
        var command = new WorkflowExecutionCommand(
            $"command-{index}",
            "wfexec-scopes",
            kind,
            Now,
            null,
            new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            $"envelope-{index}",
            "wfexec-scopes",
            command,
            $"wfexec-scopes:command-{index}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            Now,
            index,
            partition: partition);
    }

    private static void ReplaceWithScopedRuntimeStores(IServiceCollection services)
    {
        ReplaceScoped<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>(services);
        ReplaceScoped<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>(services);
        ReplaceScoped<IWorkflowExecutionStateStore, InMemoryWorkflowExecutionStateStore>(services);
        ReplaceScoped<IActivityExecutionStateStore, InMemoryActivityExecutionStateStore>(services);
        services.RemoveAll<InMemoryActivityExecutionInspectionStore>();
        services.RemoveAll<IActivityExecutionInspectionStore>();
        services.RemoveAll<IActivityExecutionInspectionWriter>();
        services.AddScoped<InMemoryActivityExecutionInspectionStore>();
        services.AddScoped<IActivityExecutionInspectionStore>(sp => sp.GetRequiredService<InMemoryActivityExecutionInspectionStore>());
        services.AddScoped<IActivityExecutionInspectionWriter>(sp => sp.GetRequiredService<InMemoryActivityExecutionInspectionStore>());
        ReplaceScoped<IBookmarkStateStore, InMemoryBookmarkStateStore>(services);
        ReplaceScoped<IDurableValueStateStore, InMemoryDurableValueStateStore>(services);
        ReplaceScoped<IIncidentStateStore, InMemoryIncidentStateStore>(services);
        ReplaceScoped<IExecutionLivenessStateStore, InMemoryExecutionLivenessStateStore>(services);
        ReplaceScoped<IWorkflowHoldStateStore, InMemoryWorkflowHoldStateStore>(services);
        ReplaceScoped<ISchedulerStateStore, InMemorySchedulerStateStore>(services);
        ReplaceScoped<IWorkflowSchedulerWorkQueue, InMemoryWorkflowSchedulerWorkQueue>(services);
    }

    private static void AddExpressionServices(IServiceCollection services)
    {
        services.AddScoped<IPortableExpressionEvaluator, StubPortableExpressionEvaluator>();
        services.AddSingleton<IWellKnownTypeRegistry, StubWellKnownTypeRegistry>();
    }

    private sealed class ReplacementStartPolicy : IWorkflowExecutableStartPolicy
    {
        public ValueTask<WorkflowExecutableStartDecision> EvaluateAsync(
            WorkflowExecutableStartPolicyContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(WorkflowExecutableStartDecision.Allow());
    }

    private sealed class StubPortableExpressionEvaluator : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement<object?>(null));
    }

    private sealed class StubWellKnownTypeRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();

        public bool TryGetAlias(Type type, out string alias)
        {
            alias = "String";
            return type == typeof(string);
        }

        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => [typeof(string)];
        public string GetAliasOrDefault(Type type) => type == typeof(string) ? "String" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => TryGetTypeOrDefault(alias, out var type) ? type : typeof(object);

        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            type = typeof(string);
            return StringComparer.Ordinal.Equals(alias, "String");
        }
    }

    private static void ReplaceScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddScoped<TService, TImplementation>();
    }

    private sealed class SchedulerQueueScopeObservations
    {
        public ConcurrentQueue<Guid> Created { get; } = new();
        public ConcurrentQueue<Guid> Disposed { get; } = new();
        public ConcurrentQueue<string> Partitions { get; } = new();
    }

    private sealed class FakeFirstTestScopeProvider;

    private sealed class FakeSecondTestScopeProvider;

    private sealed class ScopeTrackingSchedulerWorkQueue : IWorkflowSchedulerWorkQueue, IDisposable
    {
        private readonly InMemoryWorkflowSchedulerWorkQueue _inner = new();
        private readonly SchedulerQueueScopeObservations _observations;
        private readonly Guid _instanceId = Guid.NewGuid();

        public ScopeTrackingSchedulerWorkQueue(
            SchedulerQueueScopeObservations observations,
            IWorkflowExecutionPartitionAccessor partitionAccessor)
        {
            _observations = observations;
            observations.Created.Enqueue(_instanceId);
            observations.Partitions.Enqueue(partitionAccessor.Current.Value);
        }

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            _inner.EnqueueAsync(workItem, cancellationToken);

        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(query, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            _inner.DequeueAsync(workflowExecutionId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            _inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

        public void Dispose() => _observations.Disposed.Enqueue(_instanceId);
    }
}
