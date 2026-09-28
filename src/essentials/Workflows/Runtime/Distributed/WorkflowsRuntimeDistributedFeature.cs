using CShells.Features;
using Elsa.Cluster.Core.Contracts;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Distributed;

/// <summary>
/// Opt-in host feature that makes the workflow-execution actor subsystem clustered. It replaces the single active
/// <see cref="IWorkflowExecutionActorProvider"/> with <see cref="DistributedWorkflowExecutionActorProvider"/> (a single
/// active provider per app, constitution S=2.6), registers shared in-memory placement/transport defaults,
/// and runs the placement pump that renews leases and re-drives cross-node backlog.
/// </summary>
/// <remarks>
/// This unit ships in-memory defaults for the two-node harness shape. Persistence features can replace either contract;
/// the EF Core leaves supply scoped durable implementations using the frozen
/// <c>executionCommandTransport</c> wire format. The pump is an <see cref="IRecurringTask"/>, so this feature depends
/// on the Tasks feature for its execution lifecycle and opens a fresh operation scope per sweep.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "WorkflowsRuntimeDistributed",
    DisplayName = "Workflows Runtime Distributed",
    Description = "Clusters the workflow-execution actor subsystem: replaces the in-process actor provider with a distributed one that routes commands by per-execution placement lease and a durable cross-node command transport, and runs a placement pump that renews leases and re-drives backlog on failover. Double execution is prevented by the single-writer fencing token at checkpoint commit. Compose alongside the Tasks feature.",
    DependsOn = new object[] { "Tasks" })]
public sealed class WorkflowsRuntimeDistributedFeature : IShellFeature
{
    private int _maxExecutionsPerSweep = 100;
    private int _transportLeaseBatchSize = 100;

    /// <summary>
    /// No longer a source of identity: the routing identity is this member's cluster host id (spec 184, FR-001). A value
    /// that differs from the host id refuses the shell (FR-003); a value equal to it is accepted. The setting is kept,
    /// rather than deleted or made to throw in its setter, because CShells silently ignores an unknown key and swallows
    /// a setter's exception, and either would accept a configuration that still names a different id.
    /// </summary>
    [ManifestSetting(DisplayName = "Node ID", Description = "Retired: the routing identity is the cluster host id (Elsa:Cluster:Membership:HostId). A value that differs from the host id refuses the shell.", Category = "Runtime")]
    public string? NodeId { get; set; }

    [ManifestSetting(DisplayName = "Lease duration (seconds)", Description = "Placement and transport visibility lease TTL. A node that stops renewing within this window loses its executions to a survivor.", Category = "Runtime", DefaultValue = "30")]
    public double LeaseDurationSeconds { get; set; } = 30;

    [ManifestSetting(DisplayName = "Sweep interval (seconds)", Description = "Baseline seconds between placement sweeps (lease renewal + backlog re-drive) while the node is healthy.", Category = "Runtime", DefaultValue = "10")]
    public double SweepIntervalSeconds { get; set; } = 10;

    [ManifestSetting(DisplayName = "Max backoff interval (minutes)", Description = "Upper bound the sweep interval widens to under sustained failure.", Category = "Runtime", DefaultValue = "5")]
    public double MaxBackoffIntervalMinutes { get; set; } = 5;

    [ManifestSetting(DisplayName = "Max executions per sweep", Description = "Hard cap on executions claimed and re-driven per sweep, bounding dispatch bursts.", Category = "Runtime", DefaultValue = "100")]
    public int MaxExecutionsPerSweep
    {
        get => _maxExecutionsPerSweep;
        set => _maxExecutionsPerSweep = DistributedRuntimeQueryLimits.ValidateTake(
            value,
            nameof(MaxExecutionsPerSweep));
    }

    [ManifestSetting(DisplayName = "Transport lease batch size", Description = "Maximum transport items leased per owned execution per sweep.", Category = "Runtime", DefaultValue = "100")]
    public int TransportLeaseBatchSize
    {
        get => _transportLeaseBatchSize;
        set => _transportLeaseBatchSize = DistributedRuntimeQueryLimits.ValidateTake(
            value,
            nameof(TransportLeaseBatchSize));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPersistenceCore();

        // One routing identity, the member's host id, for the placement lease, the transport item lease and the
        // execution lease alike (spec 184, FR-001), so one departure verdict reclaims all three.
        services.AddShellRunnabilitySource();
        services.AddOptions<ExecutionPlacementOptions>()
            .Configure<IClusterMembership>((options, membership) =>
            {
                options.NodeId = membership.GetLocalStanding().Identity.HostId;
                options.LeaseDuration = TimeSpan.FromSeconds(LeaseDurationSeconds);
            });
        services.AddSingleton<IValidateOptions<ExecutionPlacementOptions>>(new DistributedRuntimeNodeIdValidator(NodeId));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateDistributedRuntimeIdentityStartupTask>());
        services.Replace(ServiceDescriptor.Singleton(sp => new RuntimeExecutionOwnershipOptions
        {
            OwnerId = sp.GetRequiredService<ExecutionPlacementOptions>().NodeId
        }));

        services.Configure<ExecutionPlacementPumpOptions>(options =>
        {
            options.SweepInterval = TimeSpan.FromSeconds(SweepIntervalSeconds);
            options.MaxBackoffInterval = TimeSpan.FromMinutes(MaxBackoffIntervalMinutes);
            options.MaxExecutionsPerSweep = MaxExecutionsPerSweep;
            options.TransportLeaseBatchSize = TransportLeaseBatchSize;
        });

        // Shared state plus scope-bound adapters preserve partition isolation while retaining one process-wide
        // cluster view. A durable persistence feature replaces the two scoped store contracts.
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<ExecutionPlacementOptions>>().Value);
        services.TryAddSingleton<InMemoryExecutionPlacementState>();
        var placementBackend = ExecutionPlacementStoreBackend.Find(services);
        if (placementBackend is not null)
        {
            placementBackend.EnsureOwnsRegisteredContract(services);
        }
        else if (!ExecutionPlacementStoreBackend.HasRegisteredContract(services))
        {
            var placementDescriptor = ServiceDescriptor.Scoped<IExecutionPlacementStore>(sp => new InMemoryExecutionPlacementStore(
                sp.GetRequiredService<InMemoryExecutionPlacementState>(),
                sp.GetRequiredService<IPersistenceAccessContextAccessor>()));
            services.Add(placementDescriptor);
            ExecutionPlacementStoreBackend.Register(
                services,
                new ExecutionPlacementStoreBackend(ExecutionPlacementStoreBackend.InMemory, placementDescriptor));
        }
        services.TryAddScoped<IExecutionPlacementService, ExecutionPlacementService>();
        services.TryAddSingleton<InMemoryExecutionCommandTransportState>();
        var commandDescriptor = ServiceDescriptor.Scoped<IExecutionCommandTransport>(sp => new InMemoryExecutionCommandTransport(
            sp.GetRequiredService<InMemoryExecutionCommandTransportState>(),
            sp.GetRequiredService<IPersistenceAccessContextAccessor>()));
        var commandBackend = ExecutionCommandTransportBackend.Find(services);
        if (commandBackend is not null)
            commandBackend.EnsureOwnsRegisteredContract(services);
        else if (!ExecutionCommandTransportBackend.HasRegisteredContract(services))
        {
            services.Add(commandDescriptor);
            ExecutionCommandTransportBackend.Register(
                services,
                new ExecutionCommandTransportBackend(ExecutionCommandTransportBackend.InMemory, commandDescriptor));
        }
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IWorkflowDispatchDurabilityEvidence, ProcessLocalDistributionEvidence>());
        // The in-memory defaults remain usable for local routing, but they do not claim a provider-admitted
        // checkpoint fence. A durable persistence leaf replaces this evidence after its selected path is admitted.
        services.TryAddSingleton<IWorkflowExecutionLeaseFencingCapability>(ProcessLocalLeaseFencingCapability.Instance);

        // Compose the in-process provider as the local-drain engine, then replace the single active actor provider
        // registration with the distributed one (S=2.6 single active provider). Keeping the in-process provider as a
        // concrete registration avoids a self-referential resolution of IWorkflowExecutionActorProvider. The composed
        // local provider inherits the same terminal-eviction options (#542 / spec 128); driving a local drain through
        // the distributed provider still bounds the local mailbox registry, and the distributed PassivateAsync releases
        // the placement lease when the resumption reaper collects a lingering terminal execution.
        services.TryAddSingleton<RuntimeActorEvictionOptions>();
        services.TryAddSingleton(sp => new InProcessWorkflowExecutionActorProvider(
            sp.GetRequiredService<IWorkflowExecutionCommandExecutor>(),
            sp.GetRequiredService<RuntimeActorEvictionOptions>()));
        // Placement as a membership query (spec 184): the gate both claim paths ask, what the pump reclaims and reports,
        // and the contracts through which the runtime's core and Attention consume them without referencing this leaf.
        services.TryAddSingleton(JoinSweepLedger.Process);
        services.TryAddSingleton(DistributedRuntimeShell.From);
        services.TryAddSingleton(ShellSuccession.From);
        services.TryAddSingleton<ExecutionPlacementRequirementResolver>();
        services.TryAddSingleton<ExecutionPlacementGate>();
        services.TryAddSingleton<ReclaimedExecutionRegistry>();
        services.TryAddSingleton<UnplaceableWorkRegistry>();
        services.TryAddSingleton<HostIdReclaimer>();
        services.TryAddSingleton<ExecutionPlacementMembership>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeRecoveryCandidateSource, ReclaimedRecoveryCandidateSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowRuntimePlacementAttention, UnplaceableWorkAttention>());

        services.TryAddSingleton(sp => new DistributedWorkflowExecutionActorProvider(
            sp.GetRequiredService<InProcessWorkflowExecutionActorProvider>(),
            sp.GetRequiredService<IPersistenceOperationScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IWorkflowExecutionLeaseFencingCapability>(),
            sp.GetRequiredService<ExecutionPlacementGate>()));
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionActorProvider>(sp => sp.GetRequiredService<DistributedWorkflowExecutionActorProvider>()));

        services.AddSingleton<IRecurringTask>(sp => new ExecutionPlacementPumpTask(
            sp.GetRequiredService<IWorkflowExecutionActorProvider>(),
            sp.GetRequiredService<IPersistenceScopeRunner>(),
            sp.GetRequiredService<IOptions<ExecutionPlacementOptions>>(),
            sp.GetRequiredService<IOptions<ExecutionPlacementPumpOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ExecutionPlacementPumpTask>>(),
            sp.GetRequiredService<ExecutionPlacementMembership>()));
    }

    private sealed class ProcessLocalDistributionEvidence : IWorkflowDispatchDurabilityEvidence
    {
        public string Component => WorkflowDispatchDurabilityComponents.Distribution;
        public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.ProcessLocal;
    }

    private sealed class ProcessLocalLeaseFencingCapability : IWorkflowExecutionLeaseFencingCapability
    {
        public static readonly ProcessLocalLeaseFencingCapability Instance = new();

        public bool IsAvailable => false;
    }
}
