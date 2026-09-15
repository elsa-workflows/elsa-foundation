using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Services;
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
            if (preserveEntityFrameworkReviewStore)
                existingBackend!.EnsureOwnsRegisteredContract(services);

            var existingPolicyProjectionBackend = PublicationPolicyProjectionStoreBackend.Find(services);
            var preserveEntityFrameworkPolicyProjection = existingPolicyProjectionBackend?.Name == PublicationPolicyProjectionStoreBackend.EntityFramework;
            if (preserveEntityFrameworkPolicyProjection)
                existingPolicyProjectionBackend!.EnsureOwnsRegisteredContracts(services);

            if (!preserveEntityFrameworkReviewStore)
            {
                if (existingBackend is not null)
                    existingBackend.RemoveOwnedArtifacts(services);
                else
                    PublicationSnapshotReviewStoreBackend.EnsureNoUnownedRegistrations(services);
            }
            if (!preserveEntityFrameworkPolicyProjection)
            {
                if (existingPolicyProjectionBackend is not null)
                    existingPolicyProjectionBackend.RemoveOwnedArtifacts(services);
                else
                    PublicationPolicyProjectionStoreBackend.EnsureNoUnownedRegistrations(services);
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

            AddGroundworkFamily(
                services,
                PublishingPersistenceFamilyBackend.PublicationRecords,
                PublishingPersistenceFamilyBackend.PublicationRecordContracts,
                () => services.Add(ServiceDescriptor.Scoped<IPublicationRecordStore, GroundworkPublicationRecordStore>()));
            if (!preserveEntityFrameworkPolicyProjection)
            {
                var policy = ReplaceScoped<IPublicationPolicyStore, GroundworkPublicationPolicyStore>(services);
                var projectionIntent = ReplaceScoped<IPublicationProjectionIntentStore, GroundworkPublicationProjectionIntentStore>(services);
                PublicationPolicyProjectionStoreBackend.Register(
                    services,
                    new PublicationPolicyProjectionStoreBackend(
                        PublicationPolicyProjectionStoreBackend.Groundwork,
                        [policy, projectionIntent]));
            }
            if (!preserveEntityFrameworkReviewStore)
            {
                var snapshotReviewDescriptor = ReplaceScoped<IPublicationSnapshotReviewStore, GroundworkPublicationSnapshotReviewStore>(services);
                PublicationSnapshotReviewStoreBackend.Register(
                    services,
                    new PublicationSnapshotReviewStoreBackend(PublicationSnapshotReviewStoreBackend.Groundwork, snapshotReviewDescriptor));
            }
            AddGroundworkFamily(
                services,
                PublishingPersistenceFamilyBackend.ActivityPublicationReceipts,
                PublishingPersistenceFamilyBackend.ActivityPublicationReceiptContracts,
                () => services.Add(ServiceDescriptor.Scoped<IActivityPublicationReceiptStore, GroundworkActivityPublicationReceiptStore>()));
            AddGroundworkFamily(
                services,
                PublishingPersistenceFamilyBackend.ActivityDraftTestRuns,
                PublishingPersistenceFamilyBackend.ActivityDraftTestRunContracts,
                () => services.Add(ServiceDescriptor.Scoped<IActivityDraftTestRunStore, GroundworkActivityDraftTestRunStore>()));
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

    /// <summary>
    /// Registers the Groundwork reusable-activity publication commands. They write the publication receipt
    /// inside their own transaction, so they belong to whichever backend owns the receipts: an explicitly
    /// selected Entity Framework backend keeps its own commands whichever order the two are composed in.
    /// </summary>
    public static IServiceCollection AddGroundworkActivityPublicationCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var snapshot = services.ToArray();
        try
        {
            AddGroundworkFamily(
                services,
                PublishingPersistenceFamilyBackend.ActivityPublicationCommands,
                ActivityPublicationCommandContracts,
                () =>
                {
                    services.AddScoped<ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>, GroundworkActivityPublicationCommand>();
                    services.AddScoped<ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>, GroundworkSourceActivityPublicationCommand>();
                });
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    internal static IReadOnlyCollection<Type> ActivityPublicationCommandContracts { get; } =
    [
        typeof(ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>),
        typeof(ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>)
    ];

    /// <summary>
    /// Registers one Publishing persistence family on Groundwork. An explicitly selected Entity Framework
    /// owner is preserved; the in-memory default is replaced; a registration no backend recorded is refused.
    /// </summary>
    private static void AddGroundworkFamily(
        IServiceCollection services,
        string family,
        IReadOnlyCollection<Type> contracts,
        Action register)
    {
        if (PublishingPersistenceFamilyBackend.Find(services, family) is { Name: PublishingPersistenceFamilyBackend.EntityFramework } entityFramework)
        {
            entityFramework.EnsureOwnsRegisteredContracts(services);
            return;
        }

        if (!PublishingPersistenceFamilyBackend.PrepareSelection(services, family, contracts, PublishingPersistenceFamilyBackend.Groundwork))
            return;

        var firstAdded = services.Count;
        register();
        PublishingPersistenceFamilyBackend.RegisterAdded(services, family, PublishingPersistenceFamilyBackend.Groundwork, contracts, firstAdded);
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
