using CShells.Features;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Scheduling;

[ShellFeature(
    name: "WorkflowsRuntimePreparedStandaloneRecurringTriggers",
    DisplayName = "Workflows Runtime Prepared Standalone Recurring Triggers",
    Description = "Accepts scoped standalone schedule occurrences through the protected Runtime source.",
    DependsOn = new object[] { "WorkflowsRuntimePreparedStandaloneSourcePump" })]
public sealed class WorkflowsRuntimePreparedStandaloneRecurringTriggersFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<RecurringTriggerPumpOptions>();
        services.TryAddSingleton<IRecurringScheduleCalculator, RecurringScheduleCalculator>();
        services.AddSingleton<IRecurringTask>(provider => new RecurringTriggerPumpTask(
            provider.GetRequiredService<IPersistenceScopeRunner>(),
            provider.GetRequiredService<IOptions<RecurringTriggerPumpOptions>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<RecurringTriggerPumpTask>>(),
            resourceOwnedOnly: true));
    }
}
