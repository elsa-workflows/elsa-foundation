using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;

public static class GroundworkPublishingStoreRegistration
{
    public static IServiceCollection AddGroundworkPublishingStores(this IServiceCollection services)
        => services.AddGroundworkPublishingStores(sp => sp.GetRequiredService<IDocumentStore>());

    /// <summary>
    /// Registers Publishing stores against a lane-specific document store. Dedicated provider features use this
    /// overload so adding Publishing durability cannot replace Runtime or Design persistence selections.
    /// </summary>
    public static IServiceCollection AddGroundworkPublishingStores(
        this IServiceCollection services,
        Func<IServiceProvider, IDocumentStore> documentStoreFactory)
    {
        ArgumentNullException.ThrowIfNull(documentStoreFactory);
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, PublishingGroundworkStorageManifestSource>());
        services.TryAddSingleton<PublishingGroundworkDocumentSerializer>();
        services.RemoveAll<IPublicationSlotStore>();
        services.AddSingleton<IPublicationSlotStore>(sp => new GroundworkPublicationSlotStore(
            documentStoreFactory(sp), sp.GetRequiredService<PublishingGroundworkDocumentSerializer>()));
        services.RemoveAll<IPublicationRecordStore>();
        services.AddSingleton<IPublicationRecordStore>(sp => new GroundworkPublicationRecordStore(
            documentStoreFactory(sp), sp.GetRequiredService<PublishingGroundworkDocumentSerializer>()));
        services.RemoveAll<IPublicationPolicyStore>();
        services.AddSingleton<IPublicationPolicyStore>(sp => new GroundworkPublicationPolicyStore(
            documentStoreFactory(sp), sp.GetRequiredService<PublishingGroundworkDocumentSerializer>()));
        services.RemoveAll<IPublicationProjectionIntentStore>();
        services.AddSingleton<IPublicationProjectionIntentStore>(sp => new GroundworkPublicationProjectionIntentStore(
            documentStoreFactory(sp), sp.GetRequiredService<PublishingGroundworkDocumentSerializer>()));
        services.RemoveAll<IPublicationSnapshotReviewStore>();
        services.AddSingleton<IPublicationSnapshotReviewStore>(sp => new GroundworkPublicationSnapshotReviewStore(
            documentStoreFactory(sp), sp.GetRequiredService<PublishingGroundworkDocumentSerializer>()));
        return services;
    }
}
