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
        => services.AddGroundworkPublishingStores(
            sp => sp.GetRequiredService<IDocumentStore>(),
            sp => sp.GetRequiredService<IBoundedDocumentStore>());

    /// <summary>
    /// Registers Publishing stores against lane-specific document and certified bounded-query runtimes.
    /// Both factories must address the same admitted physical target.
    /// </summary>
    public static IServiceCollection AddGroundworkPublishingStores(
        this IServiceCollection services,
        Func<IServiceProvider, IDocumentStore> documentStoreFactory,
        Func<IServiceProvider, IBoundedDocumentStore> boundedDocumentStoreFactory)
    {
        ArgumentNullException.ThrowIfNull(documentStoreFactory);
        ArgumentNullException.ThrowIfNull(boundedDocumentStoreFactory);
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, PublishingGroundworkStorageManifestSource>());
        services.TryAddSingleton<PublishingGroundworkDocumentSerializer>();
        services.RemoveAll<IPublicationSlotStore>();
        services.AddSingleton<IPublicationSlotStore>(sp => new GroundworkPublicationSlotStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            boundedDocumentStoreFactory(sp)));
        services.RemoveAll<IPublicationRecordStore>();
        services.AddSingleton<IPublicationRecordStore>(sp => new GroundworkPublicationRecordStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            boundedDocumentStoreFactory(sp)));
        services.RemoveAll<IPublicationPolicyStore>();
        services.AddSingleton<IPublicationPolicyStore>(sp => new GroundworkPublicationPolicyStore(
            documentStoreFactory(sp), sp.GetRequiredService<PublishingGroundworkDocumentSerializer>()));
        services.RemoveAll<IPublicationProjectionIntentStore>();
        services.AddSingleton<IPublicationProjectionIntentStore>(sp => new GroundworkPublicationProjectionIntentStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            boundedDocumentStoreFactory(sp)));
        services.RemoveAll<IPublicationSnapshotReviewStore>();
        services.AddSingleton<IPublicationSnapshotReviewStore>(sp => new GroundworkPublicationSnapshotReviewStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            boundedDocumentStoreFactory(sp)));
        return services;
    }
}
