using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;

/// <summary>Registers the public-v2 activity-design stores against one Groundwork target.</summary>
public static class GroundworkActivitiesDesignStoreRegistration
{
    public static IServiceCollection AddGroundworkActivitiesDesignStores(
        this IServiceCollection services,
        string? targetName = null)
    {
        services.AddPersistenceCore();
        foreach (var unit in ActivitiesDesignStorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit, targetName);

        services.TryAddScoped(provider => new GroundworkV2ActivityDesignStore(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName));
        services.TryAddScoped<IDesignAtomicWriter, GroundworkDesignAtomicWrite>();

        services.RemoveAll<IActivityDefinitionStore>();
        services.AddScoped<IActivityDefinitionStore, GroundworkActivityDefinitionStore>();
        services.RemoveAll<IActivityDefinitionVersionStore>();
        services.AddScoped<IActivityDefinitionVersionStore, GroundworkActivityDefinitionVersionStore>();
        services.RemoveAll<IAddActivityDefinitionCommand>();
        services.AddScoped<IAddActivityDefinitionCommand, GroundworkAddActivityDefinitionCommand>();
        services.RemoveAll<IAddActivityDefinitionVersionCommand>();
        services.AddScoped<IAddActivityDefinitionVersionCommand, GroundworkAddActivityDefinitionVersionCommand>();
        services.RemoveAll<IActivityAvailabilitySettingsStore>();
        services.AddScoped<IActivityAvailabilitySettingsStore, GroundworkActivityAvailabilitySettingsStore>();
        services.RemoveAll<IActivityDefinitionManagementProjectionStore>();
        services.AddScoped<IActivityDefinitionManagementProjectionStore, GroundworkActivityDefinitionManagementProjectionStore>();

        services.RemoveAll<IActivityDefinitionLookup>();
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

        services.TryAddScoped<IIdentityGenerator, ShortIdentityGenerator>();
        services.TryAddScoped<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
        services.TryAddScoped<IActivityDefinitionFactory, ActivityDefinitionFactory>();
        services.TryAddScoped<IActivityDefinitionVersionFactory, ActivityDefinitionVersionFactory>();
        return services;
    }

    private static void Alias<TContract, TImplementation>(IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        services.RemoveAll<TContract>();
        services.AddScoped<TContract>(provider => provider.GetRequiredService<TImplementation>());
    }
}
