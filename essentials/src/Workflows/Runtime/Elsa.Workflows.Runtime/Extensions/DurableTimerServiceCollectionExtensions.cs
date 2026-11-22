using Elsa.Workflows.Runtime.Services.History;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Extensions;

/// <summary>Composes the selected timer owner and original-admission scheduler without replacing host overrides.</summary>
[RuntimeDefaultRegistration]
public static class DurableTimerServiceCollectionExtensions
{
    internal static void AddDefaultTimerStore(IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryDurableTimerStore>();
        services.TryAddScoped<IDurableTimerStore>(provider =>
        {
            if (provider.GetService<RuntimeHistoryOptions>() is null) return provider.GetRequiredService<InMemoryDurableTimerStore>();
            var state = provider.GetService<InMemoryRuntimeCheckpointStoreState>();
            var access = provider.GetService<IPersistenceAccessContextAccessor>();
            var executions = provider.GetService<IWorkflowExecutionStateStore>() is null ? null :
                RuntimeCoreServiceCollectionExtensions.ResolveUnderlyingStore<IWorkflowExecutionStateStore>(provider, typeof(CoalescingWorkflowExecutionStateStore));
            var liveness = provider.GetService<IExecutionLivenessStateStore>();
            if (state is not null && access is not null && executions is not null && liveness is not null)
            {
                var selected = new InMemoryDurableTimerStore(state, access, executions, liveness, provider.GetRequiredService<TimeProvider>());
                if (selected.SupportsHistoryResponsibility) return selected;
            }
            return provider.GetRequiredService<InMemoryDurableTimerStore>();
        });
    }

    /// <summary>Adds the default timer scheduler, retaining any explicitly selected provider or scheduler.</summary>
    public static IServiceCollection AddRuntimeDurableTimerScheduler(this IServiceCollection services)
    {
        AddDefaultTimerStore(services);
        services.TryAddScoped<IDurableTimerScheduler>(provider => new DurableTimerScheduler(
            provider.GetRequiredService<IDurableTimerStore>(), provider.GetRequiredService<IDeferredEventPublisher>(),
            provider.GetService<ILogger<DurableTimerScheduler>>())
        {
            AdmissionProviders = () => provider.GetService<IWorkflowExecutionStateStore>() is { } executions
                ? new(executions, provider.GetService<IPersistenceAccessContextAccessor>()) : null,
            HistoryProviders = () => provider.GetService<IWorkflowExecutionStateStore>() is { } executions
                ? new(executions, () => provider.GetService<IActivityExecutionStateStore>(),
                    provider.GetService<IRuntimeCheckpointCommitStore>() is null ? null :
                        RuntimeCoreServiceCollectionExtensions.ResolveUnderlyingStore<IRuntimeCheckpointCommitStore>(provider, typeof(CoalescingRuntimeCheckpointCommitStore)),
                    provider.GetService<IRuntimeHistoryObservationWriter>(), provider.GetService<IRuntimeHistoryStore>(), provider.GetService<IPersistenceAccessContextAccessor>()) : null
        });
        return services;
    }
}
