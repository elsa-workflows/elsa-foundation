using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Resumption.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Resumption;

/// <summary>
/// Opt-in host feature that completes the durable-resumption contract. Durable runtime persistence
/// providers keep checkpoints, post-commit outbox items, and queued scheduler work across a crash;
/// this feature registers the background pump that periodically re-delivers stranded outbox items and
/// re-drives executions whose durable backlog would otherwise never be drained.
/// </summary>
/// <remarks>
/// Compose this together with a durable runtime persistence provider (e.g. the Groundwork SQLite or
/// Unified feature). Without a durable store the pump still runs, but there is nothing durable to
/// recover; without this feature a durable store keeps the work but nothing re-drives it after a
/// restart. The pump is delivered as an <see cref="IRecurringTask"/>, so it depends on the Tasks
/// feature for its execution lifecycle.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "WorkflowsRuntimeResumption",
    DisplayName = "Workflows Runtime Resumption",
    Description = "Background pump that periodically re-delivers stranded post-commit outbox items and re-drives workflow executions with durable scheduler-work backlog after a restart or crash. Compose alongside a durable runtime persistence provider (e.g. Groundwork SQLite) and the Tasks feature.",
    DependsOn = new object[] { "Tasks" })]
public sealed class WorkflowsRuntimeResumptionFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Sweep interval (seconds)", Description = "Baseline seconds between resumption sweeps while the host is healthy.", Category = "Runtime", DefaultValue = "10")]
    public double SweepIntervalSeconds { get; set; } = 10;

    [ManifestSetting(DisplayName = "Max backoff interval (minutes)", Description = "Upper bound the sweep interval and per-execution backoff widen to under sustained failure.", Category = "Runtime", DefaultValue = "5")]
    public double MaxBackoffIntervalMinutes { get; set; } = 5;

    [ManifestSetting(DisplayName = "Outbox batch size", Description = "Maximum post-commit outbox items delivered per sweep.", Category = "Runtime", DefaultValue = "100")]
    public int OutboxBatchSize { get; set; } = 100;

    [ManifestSetting(DisplayName = "Backlog batch size", Description = "Maximum stranded-backlog execution IDs discovered from the durable queue per sweep.", Category = "Runtime", DefaultValue = "100")]
    public int BacklogBatchSize { get; set; } = 100;

    [ManifestSetting(DisplayName = "Recovery scan batch size", Description = "Maximum recovery-scanner candidates requested per sweep.", Category = "Runtime", DefaultValue = "100")]
    public int RecoveryScanBatchSize { get; set; } = 100;

    [ManifestSetting(DisplayName = "Max executions per sweep", Description = "Hard cap on executions re-driven per sweep after discovery, bounding dispatch bursts.", Category = "Runtime", DefaultValue = "100")]
    public int MaxExecutionsPerSweep { get; set; } = 100;

    [ManifestSetting(DisplayName = "Lease timeout (minutes)", Description = "Lease-expiry threshold handed to the recovery scanner.", Category = "Runtime", DefaultValue = "5")]
    public double LeaseTimeoutMinutes { get; set; } = 5;

    [ManifestSetting(DisplayName = "Heartbeat timeout (minutes)", Description = "Heartbeat-expiry threshold handed to the recovery scanner.", Category = "Runtime", DefaultValue = "5")]
    public double HeartbeatTimeoutMinutes { get; set; } = 5;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.Configure<RuntimeResumptionOptions>(options =>
        {
            options.SweepInterval = TimeSpan.FromSeconds(SweepIntervalSeconds);
            options.MaxBackoffInterval = TimeSpan.FromMinutes(MaxBackoffIntervalMinutes);
            options.OutboxBatchSize = OutboxBatchSize;
            options.BacklogBatchSize = BacklogBatchSize;
            options.RecoveryScanBatchSize = RecoveryScanBatchSize;
            options.MaxExecutionsPerSweep = MaxExecutionsPerSweep;
            options.LeaseTimeout = TimeSpan.FromMinutes(LeaseTimeoutMinutes);
            options.HeartbeatTimeout = TimeSpan.FromMinutes(HeartbeatTimeoutMinutes);
        });

        services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
        services.AddSingleton<IRecurringTask>(sp => new RuntimeResumptionPumpTask(
            sp.GetRequiredService<IPersistenceScopeRunner>(),
            sp.GetRequiredService<IOptions<RuntimeResumptionOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RuntimeResumptionPumpTask>>()));
    }
}
