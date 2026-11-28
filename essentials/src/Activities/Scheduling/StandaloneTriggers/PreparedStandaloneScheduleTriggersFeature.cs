using CShells.Features;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Scheduling.StandaloneTriggers;

[ShellFeature(
    name: "PreparedStandaloneScheduleTriggers",
    DisplayName = "Prepared standalone schedule triggers",
    Description = "Enables standalone Timer and Cron producers for prepared Actor Foundation delivery.",
    DependsOn = new object[] { "WorkflowsRuntimePreparedStandaloneRecurringTriggers" })]
public sealed class PreparedStandaloneScheduleTriggersFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddStandaloneTriggerIngress<TimerScheduleSource>(new("timer", 1, ScheduleSourceContext.TimerFamily, ScheduleSourceContext.Schema));
        services.AddStandaloneTriggerIngress<CronScheduleSource>(new("cron", 1, ScheduleSourceContext.CronFamily, ScheduleSourceContext.Schema));
        services.TryAddScoped<ScheduleSourceContext>();
        services.TryAddScoped<IStandaloneTriggerSourceContext<TimerScheduleSource>>(provider => provider.GetRequiredService<ScheduleSourceContext>());
        services.TryAddScoped<IStandaloneTriggerSourceContext<CronScheduleSource>>(provider => provider.GetRequiredService<ScheduleSourceContext>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStandaloneSchedulePlanner, ScheduleResourcePlanner>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStandaloneScheduleSource, ScheduleResourceSource>());
    }
}
