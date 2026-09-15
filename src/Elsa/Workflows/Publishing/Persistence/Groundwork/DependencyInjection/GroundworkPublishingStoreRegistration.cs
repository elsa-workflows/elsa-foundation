using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the publishing ports against public Groundwork v2 storage units.
/// <para>
/// Publishing owns its own documents — records, policies, projection intents, snapshot reviews and receipts —
/// in the target named here. Activation slots belong to the Runtime store family. A reusable-activity
/// publication also writes design and
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
        var snapshot = services.ToArray();
        var registry = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkStorageUnitRegistry>()
            .SingleOrDefault();
        var registrySnapshot = registry?.Registrations.ToArray();
        var bindings = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkManifestBindings>()
            .SingleOrDefault();
        var bindingsSnapshot = bindings?.Capture();
        try
        {
            var existingBackend = PublicationSnapshotReviewStoreBackend.Find(services);
            var preserveEntityFrameworkReviewStore = existingBackend?.Name == PublicationSnapshotReviewStoreBackend.EntityFramework;
            if (!preserveEntityFrameworkReviewStore)
            {
                if (existingBackend is not null)
                    existingBackend.RemoveOwnedArtifacts(services);
                else
                    PublicationSnapshotReviewStoreBackend.EnsureNoUnownedRegistrations(services);
            }

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
            if (!preserveEntityFrameworkReviewStore)
            {
                var snapshotReviewDescriptor = ReplaceScoped<IPublicationSnapshotReviewStore, GroundworkPublicationSnapshotReviewStore>(services);
                PublicationSnapshotReviewStoreBackend.Register(
                    services,
                    new PublicationSnapshotReviewStoreBackend(PublicationSnapshotReviewStoreBackend.Groundwork, snapshotReviewDescriptor));
            }
            ReplaceScoped<IActivityPublicationReceiptStore, GroundworkActivityPublicationReceiptStore>(services);
            ReplaceScoped<IActivityDraftTestRunStore, GroundworkActivityDraftTestRunStore>(services);
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            if (registry is not null && registrySnapshot is not null)
                registry.Restore(registrySnapshot);
            if (bindings is not null && bindingsSnapshot is not null)
                bindings.Restore(bindingsSnapshot);
            throw;
        }
    }

    private static ServiceDescriptor ReplaceScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        var descriptor = ServiceDescriptor.Scoped<TService, TImplementation>();
        services.Add(descriptor);
        return descriptor;
    }
}
