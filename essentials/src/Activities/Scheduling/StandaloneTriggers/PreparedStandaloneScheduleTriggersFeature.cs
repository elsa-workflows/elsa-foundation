using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Scheduling.StandaloneTriggers;

[ShellFeature(
    name: "PreparedStandaloneScheduleTriggers",
    DisplayName = "Prepared standalone schedule triggers",
    Description = "Enables standalone Timer and Cron producers for prepared Actor Foundation delivery.",
    DependsOn = new object[] { "WorkflowsRuntimePreparedStandaloneRecurringTriggers" })]
public sealed class PreparedStandaloneScheduleTriggersFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        StandaloneScheduleTriggersFeature.AddServices(services);
}
