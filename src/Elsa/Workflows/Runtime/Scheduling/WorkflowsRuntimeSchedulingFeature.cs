using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Opt-in host feature that fires durable timers. It registers the in-memory <see cref="IDurableTimerStore"/>
/// default (swapped for a restart-surviving store by a durable persistence provider), the activity-facing
/// <see cref="IDurableTimerScheduler"/>, and the background <see cref="DurableTimerPumpTask"/> that resumes
/// suspended workflows at their due time through the existing bookmark-resume dispatcher.
/// </summary>
/// <remarks>
/// Compose this together with a durable runtime persistence provider (e.g. the Groundwork SQLite or Unified
/// feature) to make a <c>Delay</c> survive a restart; without a durable store the pump still runs but timers
/// live only in memory. The pump is delivered as an <see cref="IRecurringTask"/>, so it depends on the Tasks
/// feature for its execution lifecycle. The runtime API feature must also be composed — the pump resolves
/// <see cref="IBookmarkResumeDispatcher"/> from it.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "WorkflowsRuntimeScheduling",
    DisplayName = "Workflows Runtime Scheduling",
    Description = "Background pump that fires durable timers, resuming suspended workflows (e.g. Delay) at their due time through the bookmark-resume dispatcher. Compose alongside a durable runtime persistence provider (e.g. Groundwork SQLite) and the Tasks feature to make timers restart-durable.",
    DependsOn = new object[] { "Tasks" })]
public sealed class WorkflowsRuntimeSchedulingFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Sweep interval (seconds)", Description = "Baseline seconds between timer sweeps while the host is healthy.", Category = "Runtime", DefaultValue = "10")]
    public double SweepIntervalSeconds { get; set; } = 10;

    [ManifestSetting(DisplayName = "Max backoff interval (minutes)", Description = "Upper bound the sweep interval and per-timer backoff widen to under sustained failure.", Category = "Runtime", DefaultValue = "5")]
    public double MaxBackoffIntervalMinutes { get; set; } = 5;

    [ManifestSetting(DisplayName = "Max timers per tick", Description = "Hard cap on due timers loaded and fired per sweep, bounding both the store scan and dispatch bursts.", Category = "Runtime", DefaultValue = "100")]
    public int MaxTimersPerTick { get; set; } = 100;

    [ManifestSetting(DisplayName = "Timer claim visibility (seconds)", Description = "Visibility lease renewed while a claim-capable durable timer store dispatches one timer.", Category = "Runtime", DefaultValue = "60")]
    public double ClaimVisibilitySeconds { get; set; } = 60;

    [ManifestSetting(DisplayName = "Not-found grace (seconds)", Description = "Grace after a timer's due time before a no-matching-bookmark dispatch deletes the timer (covers a very short delay racing its own bookmark commit).", Category = "Runtime", DefaultValue = "30")]
    public double NotFoundGraceSeconds { get; set; } = 30;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.Configure<DurableTimerPumpOptions>(options =>
        {
            options.SweepInterval = TimeSpan.FromSeconds(SweepIntervalSeconds);
            options.MaxBackoffInterval = TimeSpan.FromMinutes(MaxBackoffIntervalMinutes);
            options.MaxTimersPerTick = MaxTimersPerTick;
            options.ClaimVisibilityTimeout = TimeSpan.FromSeconds(ClaimVisibilitySeconds);
            options.NotFoundGrace = TimeSpan.FromSeconds(NotFoundGraceSeconds);
        });

        // A controllable clock underpins due-time, due-filtering, and grace checks so tests (and hosts that
        // supply their own TimeProvider) drive time deterministically. TryAdd defers to the runtime feature's
        // registration when composed together.
        services.TryAddSingleton(TimeProvider.System);

        // In-memory default; a durable persistence provider swaps IDurableTimerStore for a scoped,
        // restart-surviving adapter.
        services.TryAddSingleton<IDurableTimerStore, InMemoryDurableTimerStore>();
        services.TryAddScoped<IDurableTimerScheduler, DurableTimerScheduler>();

        services.AddSingleton<IRecurringTask>(sp => new DurableTimerPumpTask(
            sp.GetRequiredService<IPersistenceScopeRunner>(),
            sp.GetRequiredService<IOptions<DurableTimerPumpOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<DurableTimerPumpTask>>()));
    }
}
