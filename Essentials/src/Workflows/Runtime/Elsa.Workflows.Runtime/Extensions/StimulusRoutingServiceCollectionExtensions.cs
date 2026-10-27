using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Extensions;

/// <summary>Registers the existing ordinary router and its source-owned saved-cohort path for non-HTTP or API hosts.</summary>
public static class StimulusRoutingServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowStimulusRouter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        BookmarkStateStoreBackend.TryRegisterDefaultStimulusIndex(services);
        services.TryAddScoped<IGlobalBookmarkStimulusLookup, GlobalBookmarkStimulusLookup>();
        services.TryAddScoped<StimulusDeliveryServices>();
        services.TryAddScoped<IStimulusRouter>(provider => new StimulusRouter(
            provider.GetRequiredService<IWorkflowTriggerBindingStore>(), provider.GetRequiredService<IGlobalBookmarkStimulusLookup>(),
            provider.GetRequiredService<IWorkflowStartDispatcher>(), provider.GetRequiredService<IBookmarkResumeDispatcher>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<StimulusDeliveryServices>()));
        return services;
    }
}
