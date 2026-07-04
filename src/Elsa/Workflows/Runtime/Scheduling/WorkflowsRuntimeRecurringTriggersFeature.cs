using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Opt-in host feature that fires recurring start triggers — Timer/Cron activities that start a <i>new</i>
/// workflow instance on a schedule (W16), the counterpart to the durable-timer pump that resumes existing
/// instances. It registers the in-memory <see cref="IRecurringTriggerScheduleStore"/> default (swapped for a
/// restart-surviving store by a durable persistence provider), the <see cref="IRecurringScheduleCalculator"/>,
/// the background <see cref="RecurringTriggerPumpTask"/>, and decorates the publish-time
/// <see cref="IWorkflowTriggerIndexer"/> so publishing a workflow with a Timer/Cron trigger also writes its
/// recurring schedule.
/// </summary>
/// <remarks>
/// Depends on <c>WorkflowsRuntimeTriggers</c> for the trigger indexer it decorates and the
/// <see cref="IStimulusRouter"/> the pump starts instances through, and on the Tasks feature for the pump's
/// recurring-task lifecycle. Compose alongside a durable runtime persistence provider (e.g. the Groundwork
/// feature) to make recurring schedules survive a restart; without one the pump still runs but schedules live
/// only in memory until the workflow is republished.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Triggers")]
[ShellFeature(
    name: "WorkflowsRuntimeRecurringTriggers",
    DisplayName = "Workflows Runtime Recurring Triggers",
    Description = "Background pump that fires recurring Timer/Cron start triggers, starting new workflow instances on a schedule through the stimulus router. Populates the recurring-schedule store at publish time. Compose alongside the Workflows Runtime Triggers feature and the Tasks feature.",
    DependsOn = new object[] { "WorkflowsRuntimeTriggers", "Tasks" })]
public sealed class WorkflowsRuntimeRecurringTriggersFeature : IShellFeature
{
    [ManifestSetting(DisplayName = "Sweep interval (seconds)", Description = "Baseline seconds between recurring-trigger sweeps while the host is healthy.", Category = "Runtime", DefaultValue = "10")]
    public double SweepIntervalSeconds { get; set; } = 10;

    [ManifestSetting(DisplayName = "Max backoff interval (minutes)", Description = "Upper bound the sweep interval widens to under sustained failure.", Category = "Runtime", DefaultValue = "5")]
    public double MaxBackoffIntervalMinutes { get; set; } = 5;

    [ManifestSetting(DisplayName = "Max schedules per tick", Description = "Hard cap on due schedules claimed and fired per sweep.", Category = "Runtime", DefaultValue = "100")]
    public int MaxSchedulesPerTick { get; set; } = 100;

    public void ConfigureServices(IServiceCollection services)
    {
        services.Configure<RecurringTriggerPumpOptions>(options =>
        {
            options.SweepInterval = TimeSpan.FromSeconds(SweepIntervalSeconds);
            options.MaxBackoffInterval = TimeSpan.FromMinutes(MaxBackoffIntervalMinutes);
            options.MaxSchedulesPerTick = MaxSchedulesPerTick;
        });

        // A controllable clock underpins due-filtering and next-occurrence anchoring so tests (and hosts that
        // supply their own TimeProvider) drive time deterministically. TryAdd defers to the runtime feature's
        // registration when composed together.
        services.TryAddSingleton(TimeProvider.System);

        // In-memory defaults; a durable persistence provider swaps IRecurringTriggerScheduleStore for a
        // restart-surviving bridge via RemoveAll + AddSingleton.
        services.TryAddSingleton<IRecurringTriggerScheduleStore, InMemoryRecurringTriggerScheduleStore>();
        services.TryAddSingleton<IRecurringScheduleCalculator, RecurringScheduleCalculator>();

        // Decorate the publish-time trigger indexer so publishing a Timer/Cron trigger also writes its schedule.
        // The inner WorkflowTriggerIndexer is registered by the WorkflowsRuntimeTriggers dependency; construct it
        // explicitly and wrap it, so the trigger core stays unaware of scheduling. This AddSingleton lands after
        // the dependency's TryAddSingleton, so it wins as the resolved IWorkflowTriggerIndexer.
        services.AddSingleton<IWorkflowTriggerIndexer>(sp => new RecurringTriggerScheduleIndexer(
            ActivatorUtilities.CreateInstance<WorkflowTriggerIndexer>(sp),
            sp.GetServices<IRecurringTriggerScheduleProvider>(),
            sp.GetRequiredService<IRecurringTriggerScheduleStore>(),
            sp.GetRequiredService<IRecurringScheduleCalculator>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RecurringTriggerScheduleIndexer>>()));

        services.AddSingleton<IRecurringTask, RecurringTriggerPumpTask>();
    }
}
