using CShells.Features;
using Elsa.Persistence.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed class WorkflowsRuntimeDistributedFeatureTests
{
    [Fact]
    public void RegistersPlacementTransportPumpAndReplacesActorProvider()
    {
        var services = BuildBaselineServices();

        // Simulate the core registration this feature must replace (RuntimeCoreServiceCollectionExtensions).
        services.TryAddSingleton<IWorkflowExecutionActorProvider, InProcessWorkflowExecutionActorProvider>();

        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        // S=2.6 single active provider: after the feature, the one IWorkflowExecutionActorProvider resolves to the
        // distributed provider, not the in-process one it replaced.
        Assert.IsType<DistributedWorkflowExecutionActorProvider>(provider.GetRequiredService<IWorkflowExecutionActorProvider>());
        Assert.Single(services, d => d.ServiceType == typeof(IWorkflowExecutionActorProvider));

        // Leaf contracts resolve, and the pump is a named participant in the IRecurringTask collection.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IExecutionPlacementService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>());
        Assert.Contains(provider.GetServices<IRecurringTask>(), task => task is ExecutionPlacementPumpTask);
        var distributionEvidence = Assert.Single(
            provider.GetServices<IWorkflowDispatchDurabilityEvidence>(),
            evidence => evidence.Component == WorkflowDispatchDurabilityComponents.Distribution);
        Assert.Equal(WorkflowDispatchDurabilityLevel.ProcessLocal, distributionEvidence.Level);
    }

    [Fact]
    public void ResolvesEveryRegisteredService()
    {
        var services = BuildBaselineServices();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        // TS-1 (§2.23.1): every service the feature registers must resolve.
        Assert.NotNull(provider.GetRequiredService<IWorkflowExecutionActorProvider>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IExecutionPlacementStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IExecutionPlacementService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>());
        Assert.NotNull(provider.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>().Single());
    }

    [Fact]
    public async Task Host_lifetime_actor_and_pump_open_scopes_for_placement_operations()
    {
        var services = BuildBaselineServices();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var actorProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>());

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<IExecutionPlacementService>(),
            secondScope.ServiceProvider.GetRequiredService<IExecutionPlacementService>());

        var activation = new WorkflowExecutionActorActivationRequest(
            "execution-1",
            WorkflowExecutionActorActivationReason.Start,
            DateTimeOffset.UtcNow,
            "test",
            WorkflowExecutionActorCapabilities.None);
        Assert.NotNull(await actorProvider.GetAgentAsync(activation));
        await pump.SweepAsync();
        await pump.SweepAsync();
    }

    [Fact]
    public async Task Placement_pump_resolves_and_disposes_a_fresh_scoped_transport_each_tick()
    {
        var lifecycle = new ScopedTransportLifecycleRecorder();
        var services = BuildBaselineServices();
        services.AddSingleton(lifecycle);
        services.AddScoped<IExecutionCommandTransport>(sp =>
            new LifecycleTrackingTransport(sp.GetRequiredService<ScopedTransportLifecycleRecorder>()));
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>());

        await pump.SweepAsync();
        await pump.SweepAsync();

        Assert.Equal(2, lifecycle.CreatedIds.Count);
        Assert.Equal(2, lifecycle.CreatedIds.Distinct().Count());
        Assert.Equal(
            lifecycle.CreatedIds.OrderBy(x => x),
            lifecycle.DisposedIds.OrderBy(x => x));
    }

    [Fact]
    public async Task In_memory_defaults_isolate_equal_execution_ids_by_partition()
    {
        var services = BuildBaselineServices();
        new WorkflowsRuntimeDistributedFeature { NodeId = "node-a" }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scopeFactory = provider.GetRequiredService<IPersistenceOperationScopeFactory>();
        await using var tenantA = await scopeFactory.CreateAsync(new PersistenceScope("tenant-a"));
        await using var tenantB = await scopeFactory.CreateAsync(new PersistenceScope("tenant-b"));
        var placementA = tenantA.ServiceProvider.GetRequiredService<IExecutionPlacementService>();
        var placementB = tenantB.ServiceProvider.GetRequiredService<IExecutionPlacementService>();

        var claimA = await placementA.TryClaimAsync("same-execution");
        var claimB = await placementB.TryClaimAsync("same-execution");

        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, claimA.Outcome);
        Assert.Equal(ExecutionPlacementClaimOutcome.Granted, claimB.Outcome);
        Assert.Equal(1, claimA.Lease.PlacementToken);
        Assert.Equal(1, claimB.Lease.PlacementToken);

        var now = DateTimeOffset.UtcNow;
        var transportA = tenantA.ServiceProvider.GetRequiredService<IExecutionCommandTransport>();
        var transportB = tenantB.ServiceProvider.GetRequiredService<IExecutionCommandTransport>();
        await transportA.SendAsync("same-execution", Envelope("env-a", "tenant-a", now), now);
        await transportB.SendAsync("same-execution", Envelope("env-b", "tenant-b", now), now);

        Assert.Equal(1, await transportA.CountPendingAsync("same-execution"));
        Assert.Equal(1, await transportB.CountPendingAsync("same-execution"));
        Assert.Equal(["same-execution"], await transportA.ListPendingExecutionIdsAsync(now, 10));
        Assert.Equal(["same-execution"], await transportB.ListPendingExecutionIdsAsync(now, 10));
    }

    [Fact]
    public async Task Forwarding_actor_rejects_same_execution_from_another_partition_before_transport()
    {
        var now = DateTimeOffset.UtcNow;
        var transport = new InMemoryExecutionCommandTransport();
        var actor = new ForwardingWorkflowExecutionActor(
            "same-execution",
            new WorkflowExecutionPartition("tenant-a"),
            "node-a",
            "node-b",
            transport,
            TimeProvider.System);

        var result = await actor.EnqueueAsync(Envelope("envelope-b", "tenant-b", now));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Rejected, result.Status);
        Assert.Equal("Envelope partition does not match this forwarding actor.", result.Reason);
        Assert.Equal(0, await transport.CountPendingAsync("same-execution"));
    }

    [Fact]
    public async Task Placement_pump_rejects_persisted_envelope_from_another_partition_before_dispatch_or_ack()
    {
        var now = DateTimeOffset.UtcNow;
        var transport = new CrossPartitionTransport(now);
        var services = BuildBaselineServices();
        services.AddScoped<IExecutionCommandTransport>(_ => transport);
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a"));
        new WorkflowsRuntimeDistributedFeature { NodeId = "node-a" }.ConfigureServices(services);
        var actors = new CountingActorProvider();
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionActorProvider>(actors));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => pump.SweepAsync().AsTask());

        Assert.Equal("A persisted command partition does not match the current persistence scope.", exception.Message);
        Assert.Equal(0, actors.ActivationCount);
        Assert.Equal(0, transport.AckCount);
    }

    [Fact]
    public async Task Direct_placement_pump_rejects_another_partition_before_dispatch_or_ack()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new FakeTimeProvider(now);
        var transport = new CrossPartitionTransport(now);
        var actors = new CountingActorProvider();
        var placementOptions = new ExecutionPlacementOptions { NodeId = "node-a" };
        var placement = new ExecutionPlacementService(new InMemoryExecutionPlacementStore(), clock, placementOptions);
        var pump = new ExecutionPlacementPumpTask(
            actors,
            placement,
            transport,
            new WorkflowExecutionPartition("tenant-a"),
            Microsoft.Extensions.Options.Options.Create(placementOptions),
            Microsoft.Extensions.Options.Options.Create(new ExecutionPlacementPumpOptions()),
            clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExecutionPlacementPumpTask>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => pump.SweepAsync().AsTask());

        Assert.Equal("A persisted command partition does not match the current persistence scope.", exception.Message);
        Assert.Equal(0, actors.ActivationCount);
        Assert.Equal(0, transport.AckCount);
    }

    [Fact]
    public async Task Placement_pump_sweeps_every_configured_persistence_scope()
    {
        var services = BuildBaselineServices();
        services.AddSingleton<IPersistenceScopeSource>(new TestPersistenceScopeSource("tenant-a", "tenant-b"));
        new WorkflowsRuntimeDistributedFeature { NodeId = "node-a" }.ConfigureServices(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var operationScopeFactory = provider.GetRequiredService<IPersistenceOperationScopeFactory>();
        await using (var tenantA = await operationScopeFactory.CreateAsync(new PersistenceScope("tenant-a")))
            await tenantA.ServiceProvider.GetRequiredService<IExecutionPlacementService>().TryClaimAsync("same-execution");
        await using (var tenantB = await operationScopeFactory.CreateAsync(new PersistenceScope("tenant-b")))
            await tenantB.ServiceProvider.GetRequiredService<IExecutionPlacementService>().TryClaimAsync("same-execution");

        var pump = Assert.Single(provider.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>());
        var result = await pump.SweepAsync();

        Assert.Equal(2, result.RenewedCount);
    }

    [Fact]
    public void MapsSettingsOntoOptions()
    {
        var services = BuildBaselineServices();

        new WorkflowsRuntimeDistributedFeature
        {
            NodeId = "node-x",
            LeaseDurationSeconds = 45,
            SweepIntervalSeconds = 3,
            MaxBackoffIntervalMinutes = 2,
            MaxExecutionsPerSweep = 14,
            TransportLeaseBatchSize = 7
        }.ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();
        var placement = serviceProvider.GetRequiredService<IOptions<ExecutionPlacementOptions>>().Value;
        var pump = serviceProvider.GetRequiredService<IOptions<ExecutionPlacementPumpOptions>>().Value;

        Assert.Equal("node-x", placement.NodeId);
        Assert.Equal(TimeSpan.FromSeconds(45), placement.LeaseDuration);
        Assert.Equal(TimeSpan.FromSeconds(3), pump.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), pump.MaxBackoffInterval);
        Assert.Equal(14, pump.MaxExecutionsPerSweep);
        Assert.Equal(7, pump.TransportLeaseBatchSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void RejectsFeatureAndPumpLimitsOutsideTheDeclaredMaximum(int maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowsRuntimeDistributedFeature { MaxExecutionsPerSweep = maximum });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowsRuntimeDistributedFeature { TransportLeaseBatchSize = maximum });

        var options = new ExecutionPlacementPumpOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxExecutionsPerSweep = maximum);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.TransportLeaseBatchSize = maximum);
    }

    [Fact]
    public void DeclaresTasksDependencyAndServerRuntime()
    {
        var attribute = Assert.Single(
            typeof(WorkflowsRuntimeDistributedFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsRuntimeDistributed", attribute.Name);
        Assert.Contains("Tasks", attribute.DependsOn.Select(d => d?.ToString()));
    }

    private static ServiceCollection BuildBaselineServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IWorkflowExecutionCommandExecutor>(NoopWorkflowExecutionCommandExecutor.Instance);
        return services;
    }

    private static WorkflowExecutionCommandEnvelope Envelope(
        string envelopeId,
        string partition,
        DateTimeOffset enqueuedAt)
    {
        var command = new WorkflowExecutionCommand(
            $"command-{envelopeId}",
            "same-execution",
            WorkflowExecutionCommandKind.RunSchedulerWork,
            enqueuedAt,
            null,
            new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            envelopeId,
            "same-execution",
            command,
            $"idempotency-{envelopeId}",
            WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt,
            partition: new WorkflowExecutionPartition(partition));
    }

    private sealed class TestPersistenceScopeSource(params string[] scopes) : IPersistenceScopeSource
    {
        private readonly IReadOnlyList<PersistenceScope> _scopes = scopes.Select(x => new PersistenceScope(x)).ToArray();

        public ValueTask<IReadOnlyList<PersistenceScope>> GetScopesAsync(CancellationToken cancellationToken = default) =>
            new(_scopes);
    }

    private sealed class CountingActorProvider : IWorkflowExecutionActorProvider
    {
        public int ActivationCount { get; private set; }
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(
            WorkflowExecutionActorActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            ActivationCount++;
            throw new InvalidOperationException("Actor dispatch must not be reached.");
        }

        public ValueTask PassivateAsync(
            WorkflowExecutionActorPassivationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class ScopedTransportLifecycleRecorder
    {
        private readonly object _syncRoot = new();
        private readonly List<Guid> _createdIds = [];
        private readonly List<Guid> _disposedIds = [];

        public IReadOnlyList<Guid> CreatedIds
        {
            get
            {
                lock (_syncRoot)
                    return _createdIds.ToArray();
            }
        }

        public IReadOnlyList<Guid> DisposedIds
        {
            get
            {
                lock (_syncRoot)
                    return _disposedIds.ToArray();
            }
        }

        public void RecordCreated(Guid id)
        {
            lock (_syncRoot)
                _createdIds.Add(id);
        }

        public void RecordDisposed(Guid id)
        {
            lock (_syncRoot)
                _disposedIds.Add(id);
        }
    }

    private sealed class LifecycleTrackingTransport : IExecutionCommandTransport, IDisposable
    {
        private readonly ScopedTransportLifecycleRecorder _lifecycle;
        private readonly Guid _id = Guid.NewGuid();
        private bool _disposed;

        public LifecycleTrackingTransport(ScopedTransportLifecycleRecorder lifecycle)
        {
            _lifecycle = lifecycle;
            _lifecycle.RecordCreated(_id);
        }

        public ValueTask<ExecutionCommandTransportItem> SendAsync(
            string workflowExecutionId,
            WorkflowExecutionCommandEnvelope envelope,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(
            string workflowExecutionId,
            string ownerId,
            DateTimeOffset now,
            TimeSpan leaseDuration,
            int maxItems,
            CancellationToken cancellationToken = default) =>
            new(Array.Empty<ExecutionCommandTransportItem>());

        public ValueTask<bool> AckAsync(
            string workflowExecutionId,
            string transportItemId,
            string ownerId,
            long leaseToken,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => new(false);

        public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
            DateTimeOffset now,
            int maxItems,
            CancellationToken cancellationToken = default) => new(Array.Empty<string>());

        public ValueTask<int> CountPendingAsync(
            string workflowExecutionId,
            CancellationToken cancellationToken = default) => new(0);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lifecycle.RecordDisposed(_id);
        }
    }

    private sealed class CrossPartitionTransport(DateTimeOffset now) : IExecutionCommandTransport
    {
        public int AckCount { get; private set; }

        public ValueTask<ExecutionCommandTransportItem> SendAsync(
            string workflowExecutionId,
            WorkflowExecutionCommandEnvelope envelope,
            DateTimeOffset enqueuedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(
            string workflowExecutionId,
            string ownerId,
            DateTimeOffset requestedAt,
            TimeSpan leaseDuration,
            int maxItems,
            CancellationToken cancellationToken = default) =>
            new(new[]
            {
                new ExecutionCommandTransportItem(
                    "transport-1",
                    workflowExecutionId,
                    Envelope("envelope-b", "tenant-b", now),
                    1,
                    now)
            });

        public ValueTask<bool> AckAsync(
            string workflowExecutionId,
            string transportItemId,
            string ownerId,
            long leaseToken,
            DateTimeOffset acknowledgedAt,
            CancellationToken cancellationToken = default)
        {
            AckCount++;
            return new ValueTask<bool>(true);
        }

        public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
            DateTimeOffset requestedAt,
            int maxItems,
            CancellationToken cancellationToken = default) =>
            new(new[] { "same-execution" });

        public ValueTask<int> CountPendingAsync(
            string workflowExecutionId,
            CancellationToken cancellationToken = default) =>
            new(1);
    }
}
