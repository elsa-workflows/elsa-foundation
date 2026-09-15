using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Composition;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Identity;
using StorageUnit = Groundwork.Kernel.StorageUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;

/// <summary>Registers or explicitly switches the public-v2 activity-design stores against one Groundwork target.</summary>
public static class GroundworkActivitiesDesignStoreRegistration
{
    public static IServiceCollection AddGroundworkActivitiesDesignStores(
        this IServiceCollection services,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var normalizedTarget = GroundworkTargetNames.Normalize(targetName);
        var configuration = ActivitiesDesignPersistenceBackend.Fingerprint(normalizedTarget);
        var snapshot = services.ToArray();
        var registry = FindRegistry(services);
        var registrySnapshot = registry?.Registrations.ToArray();
        var bindings = FindBindings(services);
        var bindingsSnapshot = bindings?.Capture();

        try
        {
            var existingBackend = ActivitiesDesignPersistenceBackend.Find(services);
            if (existingBackend is not null)
            {
                existingBackend.EnsureOwnsRegisteredDescriptors(services);
                EnsureAtomicWriterComposition(services, existingBackend.Name);

                if (existingBackend.Name == ActivitiesDesignPersistenceBackend.Groundwork)
                {
                    if (!existingBackend.HasConfiguration(ActivitiesDesignPersistenceBackend.Groundwork, configuration))
                        throw new InvalidOperationException("Activities Design Groundwork persistence is already registered for a different target.");
                    return services;
                }

                existingBackend.RemoveOwnedDescriptors(services);
                EnsureNoUnownedActivitiesDesignRegistrations(services);
            }
            else
            {
                EnsureNoUnownedActivitiesDesignRegistrations(services);
            }

            ActivitiesDesignPersistenceBackend.RemoveFrameworkDefaults(services);
            services.AddPersistenceCore();
            // Identity generation is a host-wide seam shared with workflow design, so it is supplied
            // only as a default and never counted among this backend's owned registrations.
            services.TryAddScoped<IIdentityGenerator, ShortIdentityGenerator>();
            services.AddGroundworkStorageLane<ActivitiesDesignGroundworkStorageManifestSource>(targetName);
            var units = ActivitiesDesignStorageManifest.CreateUnits();
            foreach (var unit in units)
                services.AddGroundworkStorageUnit(unit, targetName);

            // Persistence core and the Groundwork catalog are shared with other lanes, so they stay outside
            // the owned range. A backend switch withdraws this lane's declarations from them instead.
            var registrationStart = services.Count;

            services.TryAddScoped(provider => new GroundworkV2ActivityDesignStore(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                targetName,
                timeProvider: null,
                privilegedQueryAuditExecutor: provider.GetRequiredService<GroundworkPrivilegedQueryAuditExecutor>()));
            services.TryAddScoped<IDesignAtomicWriter, GroundworkDesignAtomicWrite>();

            services.AddScoped<IActivityDefinitionStore, GroundworkActivityDefinitionStore>();
            services.AddScoped<IActivityDefinitionVersionStore, GroundworkActivityDefinitionVersionStore>();
            services.AddScoped<IAddActivityDefinitionCommand, GroundworkAddActivityDefinitionCommand>();
            services.AddScoped<IAddActivityDefinitionVersionCommand, GroundworkAddActivityDefinitionVersionCommand>();
            services.AddScoped<IActivityAvailabilitySettingsStore, GroundworkActivityAvailabilitySettingsStore>();
            services.AddScoped<IActivityDefinitionManagementProjectionStore, GroundworkActivityDefinitionManagementProjectionStore>();
            services.AddScoped<IActivityDefinitionLookup, ActivityDefinitionLookup>();

            services.TryAddScoped<GroundworkReusableActivityStores>();
            services.TryAddScoped<GroundworkRecommendedActivityDefinitionPickerStore>();
            services.TryAddScoped<GroundworkActivityManagementProjectionWriter>();
            services.TryAddScoped<GroundworkActivityManagementProjectionRetention>();
            services.TryAddScoped<GroundworkActivityDependencyProjection>();
            services.TryAddScoped<GroundworkActivityUpgradePlanStore>();

            Alias<IActivityDefinitionAuthoringStore, GroundworkReusableActivityStores>(services);
            Alias<IActivityDefinitionDraftStore, GroundworkReusableActivityStores>(services);
            Alias<IActivityDefinitionVersionPublicationStore, GroundworkReusableActivityStores>(services);
            Alias<IRecommendedActivityDefinitionPickerStore, GroundworkRecommendedActivityDefinitionPickerStore>(services);
            Alias<IActivityDefinitionLayoutStore, GroundworkReusableActivityStores>(services);
            Alias<IActivityDraftValidationStore, GroundworkReusableActivityStores>(services);
            Alias<IActivityForkStore, GroundworkReusableActivityStores>(services);
            Alias<IActivityDirectDependencyStore, GroundworkReusableActivityStores>(services);
            Alias<ICreateActivityDefinitionCommand, GroundworkReusableActivityStores>(services);
            Alias<ISaveActivityForkCandidateCommand, GroundworkReusableActivityStores>(services);
            Alias<IPruneActivityForkCandidatesCommand, GroundworkReusableActivityStores>(services);
            Alias<IApplyActivityForkCandidateCommand, GroundworkReusableActivityStores>(services);
            Alias<IUpdateActivityDefinitionPresentationCommand, GroundworkReusableActivityStores>(services);
            Alias<ICreateActivityDraftCommand, GroundworkReusableActivityStores>(services);
            Alias<IUpdateActivityDraftPresentationCommand, GroundworkReusableActivityStores>(services);
            Alias<ICreateActivityDraftConflictCopyCommand, GroundworkReusableActivityStores>(services);
            Alias<IReplaceActivityDraftCommand, GroundworkReusableActivityStores>(services);
            Alias<IApplyActivityContractProposalCommand, GroundworkReusableActivityStores>(services);
            Alias<IDiscardActivityDraftCommand, GroundworkReusableActivityStores>(services);
            Alias<IStoreActivityDraftValidationCommand, GroundworkReusableActivityStores>(services);
            Alias<IChangeActivityVersionLifecycleCommand, GroundworkReusableActivityStores>(services);
            Alias<ISetActivityDefinitionRecommendationCommand, GroundworkReusableActivityStores>(services);

            Alias<IActivityDependencyProjectionStore, GroundworkActivityDependencyProjection>(services);
            Alias<IActivityDependencyProjectionRebuilder, GroundworkActivityDependencyProjection>(services);
            Alias<IActivityUpgradePlanStore, GroundworkActivityUpgradePlanStore>(services);
            Alias<IActivityUpgradeApplyReceiptStore, GroundworkActivityUpgradePlanStore>(services);

            services.TryAddScoped<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
            services.TryAddScoped<IActivityDefinitionFactory, ActivityDefinitionFactory>();
            services.TryAddScoped<IActivityDefinitionVersionFactory, ActivityDefinitionVersionFactory>();

            ActivitiesDesignPersistenceBackend.Register(services, new ActivitiesDesignPersistenceBackend(
                ActivitiesDesignPersistenceBackend.Groundwork,
                configuration,
                services.Skip(registrationStart).ToArray(),
                collection => WithdrawDeclarations(collection, units, normalizedTarget)));
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

    private static void Alias<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
        => services.AddScoped<TContract>(provider => provider.GetRequiredService<TImplementation>());

    private static void WithdrawDeclarations(IServiceCollection services, IEnumerable<StorageUnit> units, string targetName)
    {
        foreach (var unit in units)
            services.RemoveGroundworkStorageUnit(unit.Id.Value, targetName);
        services.RemoveGroundworkStorageLane<ActivitiesDesignGroundworkStorageManifestSource>();
    }

    private static void EnsureNoUnownedActivitiesDesignRegistrations(IServiceCollection services)
    {
        if (ActivitiesDesignPersistenceBackend.HasUnownedReplacementContract(services))
            throw new InvalidOperationException("Activities Design already has a persistence contract registered; remove it explicitly before selecting Groundwork.");

        EnsureAtomicWriterComposition(services, ActivitiesDesignPersistenceBackend.Groundwork);
        if (services.Any(IsGroundworkArtifactDescriptor))
            throw new InvalidOperationException("Activities Design already has a Groundwork persistence artifact registered; select it through its backend registration.");
    }

    private static void EnsureAtomicWriterComposition(IServiceCollection services, string backendName)
    {
        var marked = services
            .Where(descriptor => descriptor.ServiceType.IsDefined(typeof(ActivityDesignPersistenceReplacementContractAttribute), inherit: false))
            .ToArray();
        if (marked.Any(descriptor => !IsAtomicWriterServiceType(descriptor.ServiceType, backendName)))
            throw new InvalidOperationException("Activities Design already has a conflicting persistence replacement contract registered; select exactly one backend.");
        if (marked.Length > 1)
            throw new InvalidOperationException("Activities Design Groundwork atomic persistence has conflicting replacement-contract registrations; select exactly one implementation.");
    }

    private static bool IsAtomicWriterServiceType(Type serviceType, string backendName)
    {
        if (serviceType.Name != "IDesignAtomicWriter")
            return false;

        var namespaceName = serviceType.Namespace ?? string.Empty;
        return backendName == ActivitiesDesignPersistenceBackend.EntityFramework
            ? namespaceName.EndsWith(".EntityFramework" + "Core", StringComparison.Ordinal)
            : namespaceName.EndsWith(".Groundwork", StringComparison.Ordinal);
    }

    private static bool IsGroundworkArtifactDescriptor(ServiceDescriptor descriptor)
    {
        var serviceType = descriptor.ServiceType;
        return serviceType == typeof(GroundworkV2ActivityDesignStore) ||
               serviceType == typeof(GroundworkReusableActivityStores) ||
               serviceType == typeof(GroundworkRecommendedActivityDefinitionPickerStore) ||
               serviceType == typeof(GroundworkActivityManagementProjectionWriter) ||
               serviceType == typeof(GroundworkActivityManagementProjectionRetention) ||
               serviceType == typeof(GroundworkActivityDependencyProjection) ||
               serviceType == typeof(GroundworkActivityUpgradePlanStore) ||
               serviceType == typeof(GroundworkActivityDefinitionStore) ||
               serviceType == typeof(GroundworkActivityDefinitionVersionStore) ||
               serviceType == typeof(GroundworkActivityAvailabilitySettingsStore) ||
               serviceType == typeof(GroundworkActivityDefinitionManagementProjectionStore);
    }

    private static GroundworkStorageUnitRegistry? FindRegistry(IServiceCollection services) => services
        .Where(descriptor => descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry))
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<GroundworkStorageUnitRegistry>()
        .SingleOrDefault();

    private static GroundworkManifestBindings? FindBindings(IServiceCollection services) => services
        .Where(descriptor => descriptor.ServiceType == typeof(GroundworkManifestBindings))
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<GroundworkManifestBindings>()
        .SingleOrDefault();
}
