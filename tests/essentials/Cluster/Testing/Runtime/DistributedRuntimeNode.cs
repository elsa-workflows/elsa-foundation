using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Cluster.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Testing.Runtime;

/// <summary>
/// The durable state a cluster's members share: placement, the command transport, execution liveness, execution state and
/// executables. In-memory, so one process can host several members over one "database".
/// </summary>
public sealed class SharedRuntimeState
{
    public InMemoryExecutionPlacementStore Placement { get; } = new();

    public InMemoryExecutionCommandTransport Transport { get; } = new();

    public InMemoryExecutionLivenessStateStore Liveness { get; } = new();

    public InMemoryWorkflowExecutionStateStore Executions { get; } = new();

    public InMemoryWorkflowExecutableStore Executables { get; } = new();
}

/// <summary>How to compose one member's distributed runtime.</summary>
public sealed class DistributedRuntimeNodeSetup
{
    /// <summary>The runtime consumers this member's registries activate, at their schema versions.</summary>
    public List<IRuntimeActivityConsumerCapability> Consumers { get; } = [];

    /// <summary>The durable-value storage drivers this member has.</summary>
    public HashSet<string> StorageDrivers { get; } = new(StringComparer.Ordinal);

    /// <summary>The type registry the member resolves activity type aliases through.</summary>
    public IWellKnownTypeRegistry TypeRegistry { get; set; } = new WellKnownTypeRegistry();

    /// <summary>The placement and transport lease duration. Longer than a membership liveness window by default, so a
    /// test proves that reclaim, not a lease's own expiry, freed a lease.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>The ledger of the process this member runs in: a new process gets a new one.</summary>
    public JoinSweepLedger Ledger { get; set; } = new();

    /// <summary>Further composition, applied last.</summary>
    public Action<IServiceCollection>? Configure { get; set; }
}

/// <summary>
/// One member's distributed runtime, composed through <see cref="WorkflowsRuntimeDistributedFeature"/> exactly as a
/// shell composes it, over a given membership and a <see cref="SharedRuntimeState"/>.
/// </summary>
public sealed class DistributedRuntimeNode : IAsyncDisposable
{
    private DistributedRuntimeNode(ServiceProvider services)
    {
        Services = services;
        Actors = services.GetRequiredService<IWorkflowExecutionActorProvider>();
        Pump = services.GetServices<IRecurringTask>().OfType<ExecutionPlacementPumpTask>().Single();
        Commands = services.GetRequiredService<FencedCommandExecutor>();
        Membership = services.GetRequiredService<IClusterMembership>();
    }

    public ServiceProvider Services { get; }

    public IWorkflowExecutionActorProvider Actors { get; }

    public ExecutionPlacementPumpTask Pump { get; }

    public FencedCommandExecutor Commands { get; }

    public IClusterMembership Membership { get; }

    public string HostId => Membership.GetLocalStanding().Identity.HostId;

    /// <summary>Composes a member's runtime over <paramref name="membership"/>.</summary>
    public static DistributedRuntimeNode Create(
        SharedRuntimeState shared,
        IClusterMembership membership,
        TimeProvider clock,
        Action<DistributedRuntimeNodeSetup>? configure = null)
    {
        var setup = new DistributedRuntimeNodeSetup();
        configure?.Invoke(setup);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton(membership);
        services.AddSingleton(setup.Ledger);
        services.AddScoped<IExecutionPlacementStore>(_ => shared.Placement);
        services.AddScoped<IExecutionCommandTransport>(_ => shared.Transport);
        services.AddSingleton<IExecutionLivenessStateStore>(shared.Liveness);
        services.AddSingleton<IWorkflowExecutionStateStore>(shared.Executions);
        services.AddSingleton<IWorkflowExecutableStore>(shared.Executables);
        services.AddScoped<IWorkflowExecutableReader, StoreExecutableReader>();
        foreach (var consumer in setup.Consumers)
            services.AddSingleton(consumer);
        services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry>(new FixedDriverRegistry(setup.StorageDrivers));
        services.AddSingleton(setup.TypeRegistry);
        services.AddScoped<IRuntimeRequirementChecker>(sp => new RuntimeRequirementChecker(
            sp.GetServices<IRuntimeActivityConsumerCapability>(),
            sp.GetRequiredService<IRuntimeDurableValueStorageDriverRegistry>(),
            sp.GetRequiredService<IWellKnownTypeRegistry>(),
            new JsonPayloadSerializer(new JsonPayloadConverterRegistry())));
        services.AddSingleton(sp => new FencedCommandExecutor(
            new RuntimeExecutionOwnershipService(shared.Liveness, clock, sp.GetRequiredService<RuntimeExecutionOwnershipOptions>()),
            shared.Executions));
        services.AddSingleton<IWorkflowExecutionCommandExecutor>(sp => sp.GetRequiredService<FencedCommandExecutor>());

        new WorkflowsRuntimeDistributedFeature { LeaseDurationSeconds = setup.LeaseDuration.TotalSeconds }.ConfigureServices(services);
        setup.Configure?.Invoke(services);
        return new DistributedRuntimeNode(services.BuildServiceProvider());
    }

    /// <summary>Resolves an actor for the command's execution and enqueues it, as an inbound caller on this member would.</summary>
    public async ValueTask<WorkflowExecutionCommandDispatchResult> DispatchAsync(WorkflowExecutionCommandEnvelope envelope)
    {
        var actor = await Actors.GetAgentAsync(new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: envelope.WorkflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.SchedulerWork,
            requestedAt: Services.GetRequiredService<TimeProvider>().GetUtcNow(),
            requestedBy: HostId,
            requiredCapabilities: WorkflowExecutionActorCapabilities.None,
            partition: envelope.Partition));
        return await actor.EnqueueAsync(envelope);
    }

    /// <summary>The recovery candidates this member's reclaims supplied to the recovery sweep (spec 184, FR-027).</summary>
    public async ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ReclaimedCandidatesAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<IRuntimeRecoveryCandidateSource>().OfType<ReclaimedRecoveryCandidateSource>().Single();
        return await source.ListAsync(RuntimeStorePageRequest.MaximumLimit);
    }

    /// <summary>The unplaceable work this member reports to Attention (spec 184, FR-017).</summary>
    public async ValueTask<IReadOnlyCollection<UnplaceableWorkReport>> UnplaceableWorkAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<IWorkflowRuntimePlacementAttention>().Single().ListUnplaceableWorkAsync();
    }

    /// <summary>Stops the member's placement pump as the task runner does when the shell or host stops (FR-021).</summary>
    public Task StopAsync() => ((IRecurringTask)Pump).StopAsync(CancellationToken.None);

    public ValueTask DisposeAsync() => Services.DisposeAsync();

    private sealed class StoreExecutableReader(IWorkflowExecutableStore store) : IWorkflowExecutableReader
    {
        public ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default) =>
            store.FindAsync(artifactId, cancellationToken);
    }

    private sealed class FixedDriverRegistry(IReadOnlyCollection<string> keys) : IRuntimeDurableValueStorageDriverRegistry
    {
        public IRuntimeDurableValueStorageDriver GetRequired(string driverKey) =>
            throw new NotSupportedException("The conformance runtime has no durable-value storage drivers to run.");

        public bool Contains(string driverKey) => keys.Contains(driverKey);

        public IReadOnlyCollection<string> DriverKeys => keys;
    }
}
