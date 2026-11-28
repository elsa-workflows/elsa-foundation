using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Core.Extensions;

public static class WorkflowPublicationTriggerServiceCollectionExtensions
{
    /// <summary>Registers trigger binding extraction and indexing for workflow publications.</summary>
    public static IServiceCollection AddWorkflowPublicationTriggers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        services.TryAddSingleton<IWorkflowTriggerBindingExtractor, WorkflowTriggerBindingExtractor>();
        services.TryAddScoped<IWorkflowTriggerIndexer>(provider => new WorkflowTriggerIndexer(
            provider.GetRequiredService<IWorkflowTriggerBindingExtractor>(),
            provider.GetRequiredService<IWorkflowTriggerBindingStore>(),
            provider.GetServices<IWorkflowTriggerIndexObserver>(),
            provider.GetServices<IWorkflowTriggerIndexValidator>())
        {
            EmptyRecurringProjection = provider.GetService<IRecurringTriggerScheduleStore>(),
            RecurringScheduleProviders = provider.GetServices<IRecurringTriggerScheduleProvider>()
        });
        return services;
    }
}
