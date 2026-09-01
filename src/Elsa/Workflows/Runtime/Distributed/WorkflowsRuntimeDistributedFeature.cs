using CShells.Features;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Services;
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
/// This unit ships in-memory defaults for the two-node harness shape. Persistence features can replace both contracts;
/// the Groundwork leaf supplies scoped durable implementations using the frozen
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
    Description = "Clusters the workflow-execution actor subsystem: replaces the in-process actor provider with a distributed one that routes commands by per-execution placement lease and a durable cross-node command transport, and runs a placement pump that renews leases and re-drives backlog on failover. Double execution is prevented by W5's fencing token at checkpoint commit. Compose alongside the Tasks feature.",
    DependsOn = new object[] { "Tasks" })]
public sealed class WorkflowsRuntimeDistributedFeature : IShellFeature
{
    private int _maxExecutionsPerSweep = 100;
    private int _transportLeaseBatchSize = 100;

    [ManifestSetting(DisplayName = "Node ID", Description = "Stable identity of this node as a placement owner, up to 128 well-formed UTF-16 code units. Defaults to a machine/process-derived value.", Category = "Runtime")]
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

        services.Configure<ExecutionPlacementOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(NodeId))
                options.NodeId = NodeId;
            options.LeaseDuration = TimeSpan.FromSeconds(LeaseDurationSeconds);
        });

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
        services.TryAddScoped<IExecutionPlacementStore>(sp => new InMemoryExecutionPlacementStore(
            sp.GetRequiredService<InMemoryExecutionPlacementState>(),
            sp.GetRequiredService<IPersistenceAccessContextAccessor>()));
        services.TryAddScoped<IExecutionPlacementService, ExecutionPlacementService>();
        services.TryAddSingleton<InMemoryExecutionCommandTransportState>();
        services.TryAddScoped<IExecutionCommandTransport>(sp => new InMemoryExecutionCommandTransport(
            sp.GetRequiredService<InMemoryExecutionCommandTransportState>(),
            sp.GetRequiredService<IPersistenceAccessContextAccessor>()));
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
        services.TryAddSingleton(sp => new DistributedWorkflowExecutionActorProvider(
            sp.GetRequiredService<InProcessWorkflowExecutionActorProvider>(),
            sp.GetRequiredService<IPersistenceOperationScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IWorkflowExecutionLeaseFencingCapability>()));
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionActorProvider>(sp => sp.GetRequiredService<DistributedWorkflowExecutionActorProvider>()));

        services.AddSingleton<IRecurringTask>(sp => new ExecutionPlacementPumpTask(
            sp.GetRequiredService<IWorkflowExecutionActorProvider>(),
            sp.GetRequiredService<IPersistenceScopeRunner>(),
            sp.GetRequiredService<IOptions<ExecutionPlacementOptions>>(),
            sp.GetRequiredService<IOptions<ExecutionPlacementPumpOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ExecutionPlacementPumpTask>>()));
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
