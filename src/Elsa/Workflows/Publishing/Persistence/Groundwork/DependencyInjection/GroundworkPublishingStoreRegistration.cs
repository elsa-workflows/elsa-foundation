using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the publishing ports against public Groundwork v2 storage units.
/// <para>
/// Publishing owns its own documents — publication slots, records, policies, projection intents, snapshot
/// reviews and receipts — in the target named here. A reusable-activity publication also writes design and
/// runtime material: the design rows and the publishing receipt commit together in one v2 transaction, and
/// the runtime rows follow as a replayable post-commit intent, so the path behaves the same whether or not
/// the lanes share a database.
/// </para>
/// </summary>
public static class GroundworkPublishingStoreRegistration
{
    public static IServiceCollection AddGroundworkPublishingStores(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPersistenceCore();
        services.AddGroundworkStorageLane<PublishingGroundworkStorageManifestSource>(targetName);
        foreach (var unit in PublishingGroundworkStorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit, targetName);

        services.TryAddSingleton<PublishingGroundworkDocumentSerializer>();
        services.TryAddScoped(provider => new GroundworkPublishingStorage(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName));

        ReplaceScoped<IPublicationRecordStore, GroundworkPublicationRecordStore>(services);
        ReplaceScoped<IPublicationPolicyStore, GroundworkPublicationPolicyStore>(services);
        ReplaceScoped<IPublicationProjectionIntentStore, GroundworkPublicationProjectionIntentStore>(services);
        ReplaceScoped<IPublicationSnapshotReviewStore, GroundworkPublicationSnapshotReviewStore>(services);
        ReplaceScoped<IActivityPublicationReceiptStore, GroundworkActivityPublicationReceiptStore>(services);
        ReplaceScoped<IActivityDraftTestRunStore, GroundworkActivityDraftTestRunStore>(services);
        return services;
    }

    private static void ReplaceScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddScoped<TService, TImplementation>();
    }
}
