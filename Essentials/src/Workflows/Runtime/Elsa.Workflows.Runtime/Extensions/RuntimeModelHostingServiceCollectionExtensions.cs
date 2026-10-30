using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Extensions;

/// <summary>Opt-in model dispatch through the existing runtime mailbox, scheduler and checkpoint authority.</summary>
public static class RuntimeModelHostingServiceCollectionExtensions
{
    /// <summary>Requires Events startup dispatch and qualified physical model/queue providers before admission.</summary>
    public static IServiceCollection AddRuntimeModelHosting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddWorkflowRuntime();
        if (services.Any(item => item.ServiceType == typeof(RuntimeExecutionProtocolRegistry))) return services;
        services.AddSingleton<RuntimeExecutionProtocolRegistry>();
        services.AddScoped<RuntimeExecutionProtocolSelector>();
        services.AddScoped<RuntimeExecutionProtocolRouter>();
        services.AddEventHandler<RuntimeExecutionProtocolsInitializing, RegisterRuntimeExecutionProtocols>();
        services.AddScoped<IStartupTask>(provider => new RuntimeExecutionProtocolsStartupTask(
            provider.GetRequiredService<IInlineEventPublisher>(), provider.GetRequiredService<RuntimeExecutionProtocolRegistry>(), services));
        return services;
    }
}
