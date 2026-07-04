using CShells.Features;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Scheduling;

/// <summary>
/// Scheduling activities: <see cref="Activities.Delay"/> (durable one-shot suspend/resume) plus the recurring
/// start triggers <see cref="Activities.Timer"/> and <see cref="Activities.Cron"/>. The activity types are
/// resolved by the runtime's <c>ClrActivityConstructor</c> — no per-type DI registration is required — but the
/// two recurring triggers each contribute a publish-time <see cref="IActivityTriggerStimulusProvider"/> (for the
/// trigger index) and an <see cref="IRecurringTriggerScheduleProvider"/> (for the recurring-schedule store),
/// which this feature registers.
/// </summary>
/// <remarks>
/// Depends on <c>WorkflowsRuntimeScheduling</c> (durable timer store/scheduler/pump behind <c>Delay</c>) and on
/// <c>WorkflowsRuntimeRecurringTriggers</c> (recurring-schedule store, indexer decorator, and pump behind
/// <c>Timer</c>/<c>Cron</c>), so composing this feature yields all three activities fully working.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "ActivitiesScheduling",
    DisplayName = "Activities Scheduling",
    Description = "Scheduling activities: Delay (durable one-shot suspend/resume) and the recurring Timer/Cron start triggers.",
    DependsOn = new object[] { "WorkflowsRuntimeScheduling", "WorkflowsRuntimeRecurringTriggers" }
)]
public sealed class ActivitiesSchedulingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Timer/Cron each contribute the trigger-index stimulus and the recurring schedule for their nodes.
        services.TryAddEnumerable(new[]
        {
            ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, TimerTriggerStimulusProvider>(),
            ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, CronTriggerStimulusProvider>()
        });

        services.TryAddEnumerable(new[]
        {
            ServiceDescriptor.Singleton<IRecurringTriggerScheduleProvider, TimerRecurringScheduleProvider>(),
            ServiceDescriptor.Singleton<IRecurringTriggerScheduleProvider, CronRecurringScheduleProvider>()
        });
    }
}
