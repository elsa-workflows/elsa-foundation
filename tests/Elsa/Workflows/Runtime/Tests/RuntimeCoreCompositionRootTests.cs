using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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
        typeof(IRuntimePostCommitOutboxProcessor),
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

    private WorkflowExecutionCommandEnvelope NewEnvelope(
        int index,
        WorkflowExecutionPartition? partition = null)
    {
        var command = new WorkflowExecutionCommand(
            $"command-{index}",
            "wfexec-scopes",
            WorkflowExecutionCommandKind.RunSchedulerWork,
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

        public ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(query, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            _inner.DequeueAsync(workflowExecutionId, cancellationToken);

        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) =>
            _inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);

        public void Dispose() => _observations.Disposed.Enqueue(_instanceId);
    }
}
