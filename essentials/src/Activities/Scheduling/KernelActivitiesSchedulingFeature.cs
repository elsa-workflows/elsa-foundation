using CShells.Features;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Scheduling;

/// <summary>Provides authored scheduling activities and isolated schedule Test Runs for Actor Foundation shells.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "KernelActivitiesScheduling",
    DisplayName = "Kernel Activities Scheduling",
    Description = "Supports authored Delay, Timer, and Cron on Actor Foundation and isolated fire-once Timer/Cron Test Runs.",
    DependsOn = new object[] { "WorkflowsRuntimeScheduleCalculation" })]
public sealed class KernelActivitiesSchedulingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton<KernelWorkflowScheduleActivities>();
        services.TryAddSingleton<KernelTimerTestRunIntervalPolicy>();
        services.TryAddEnumerable(new[]
        {
            ServiceDescriptor.Scoped<ITestRunTriggerDriver, TimerTestRunDriver>(),
            ServiceDescriptor.Scoped<ITestRunTriggerDriver, CronTestRunDriver>()
        });

        services.TryAddEnumerable(new[]
        {
            ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, TimerTriggerStimulusProvider>(),
            ServiceDescriptor.Singleton<IActivityTriggerStimulusProvider, CronTriggerStimulusProvider>()
        });
    }
}
