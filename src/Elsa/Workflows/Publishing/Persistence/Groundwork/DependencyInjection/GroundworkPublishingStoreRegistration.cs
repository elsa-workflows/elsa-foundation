using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
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
        services.AddPersistenceCore();
        services.AddGroundworkManifestSource<PublishingGroundworkStorageManifestSource>();
        services.TryAddSingleton<PublishingGroundworkDocumentSerializer>();
        services.RemoveAll<IPublicationSlotStore>();
        services.AddScoped<IPublicationSlotStore>(sp => new GroundworkPublicationSlotStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            boundedDocumentStoreFactory(sp)));
        services.RemoveAll<IPublicationRecordStore>();
        services.AddScoped<IPublicationRecordStore>(sp => new GroundworkPublicationRecordStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            boundedDocumentStoreFactory(sp)));
        services.RemoveAll<IPublicationPolicyStore>();
        services.AddScoped<IPublicationPolicyStore>(sp => new GroundworkPublicationPolicyStore(
            documentStoreFactory(sp), sp.GetRequiredService<PublishingGroundworkDocumentSerializer>()));
        services.RemoveAll<IPublicationProjectionIntentStore>();
        services.AddScoped<IPublicationProjectionIntentStore>(sp => new GroundworkPublicationProjectionIntentStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            boundedDocumentStoreFactory(sp)));
        services.RemoveAll<IPublicationSnapshotReviewStore>();
        services.AddScoped<IPublicationSnapshotReviewStore>(sp => new GroundworkPublicationSnapshotReviewStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            sp.GetRequiredService<IPersistenceAccessContextAccessor>(),
            boundedDocumentStoreFactory(sp)));
        services.RemoveAll<IActivityPublicationReceiptStore>();
        services.AddScoped<IActivityPublicationReceiptStore>(sp => new GroundworkActivityPublicationReceiptStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>()));
        services.RemoveAll<IActivityDraftTestRunStore>();
        services.AddScoped<IActivityDraftTestRunStore>(sp => new GroundworkActivityDraftTestRunStore(
            documentStoreFactory(sp),
            sp.GetRequiredService<PublishingGroundworkDocumentSerializer>(),
            sp.GetRequiredService<IPersistenceAccessContextAccessor>(),
            boundedDocumentStoreFactory(sp)));
        return services;
    }
}
