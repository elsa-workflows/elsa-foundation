using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;

public static class GroundworkPublishingStoreRegistration
{
    public static IServiceCollection AddGroundworkPublishingStores(this IServiceCollection services)
    {
        services.TryAddSingleton<PublishingGroundworkDocumentSerializer>();
        services.RemoveAll<IPublicationSlotStore>();
        services.AddSingleton<IPublicationSlotStore, GroundworkPublicationSlotStore>();
        services.RemoveAll<IPublicationRecordStore>();
        services.AddSingleton<IPublicationRecordStore, GroundworkPublicationRecordStore>();
        services.RemoveAll<IPublicationPolicyStore>();
        services.AddSingleton<IPublicationPolicyStore, GroundworkPublicationPolicyStore>();
        services.RemoveAll<IPublicationProjectionIntentStore>();
        services.AddSingleton<IPublicationProjectionIntentStore, GroundworkPublicationProjectionIntentStore>();
        return services;
    }
}
