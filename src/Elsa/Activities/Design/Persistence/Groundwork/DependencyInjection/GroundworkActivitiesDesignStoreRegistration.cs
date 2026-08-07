using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.DependencyInjection;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the Groundwork (document) implementations of the activity-design read ports against one named
/// Groundwork target. A provider feature declares the target and supplies the host's
/// <see cref="Elsa.Serialization.Core.IPayloadSerializer"/>; the admitted activity-management and
/// reusable-activity queries run against that target's bounded store.
/// <para>
/// The activities-design lane shares the design-operation ledger with the workflows-design lane, so both
/// must name the same target. Binding them apart is rejected when the manifest binding is recorded.
/// </para>
/// </summary>
public static class GroundworkActivitiesDesignStoreRegistration
{
    public static IServiceCollection AddGroundworkActivitiesDesignStores(
        this IServiceCollection services,
        string? targetName = null)
    {
        services.AddPersistenceCore();
        var lane = services.GroundworkLane(targetName);

        lane.Manifest<ActivitiesDesignGroundworkStorageManifestSource>();
        lane.Manifest<GroundworkDesignAtomicWriteStorageManifestSource>();
        lane.TryAdd<IDesignAtomicWriter, GroundworkDesignAtomicWrite>();

        lane.Replace<IActivityDefinitionStore, GroundworkActivityDefinitionStore>();
        lane.Replace<IActivityDefinitionVersionStore, GroundworkActivityDefinitionVersionStore>();
        lane.Replace<IAddActivityDefinitionCommand, GroundworkAddActivityDefinitionCommand>();
        lane.Replace<IAddActivityDefinitionVersionCommand, GroundworkAddActivityDefinitionVersionCommand>();
        lane.Replace<IActivityAvailabilitySettingsStore, GroundworkActivityAvailabilitySettingsStore>();
        lane.Replace<IActivityDefinitionManagementProjectionStore, GroundworkActivityDefinitionManagementProjectionStore>();

        // The lookup composes the ports above rather than a store, so it needs no target binding.
        services.RemoveAll<IActivityDefinitionLookup>();
        services.AddScoped<IActivityDefinitionLookup, ActivityDefinitionLookup>();

        // Several contracts are served by one adapter each; bind the adapter to the target once and alias.
        lane.TryAddSelf<GroundworkReusableActivityStores>();
        lane.TryAddSelf<GroundworkActivityManagementProjectionWriter>();
        lane.TryAddSelf<GroundworkActivityManagementProjectionRetention>();
        lane.TryAddSelf<GroundworkActivityDependencyProjection>();
        lane.TryAddSelf<GroundworkActivityUpgradePlanStore>();

        lane.Alias<IActivityDefinitionAuthoringStore, GroundworkReusableActivityStores>();
        lane.Alias<IActivityDefinitionDraftStore, GroundworkReusableActivityStores>();
        lane.Alias<IActivityDefinitionVersionPublicationStore, GroundworkReusableActivityStores>();
        lane.Alias<IRecommendedActivityDefinitionPickerStore, GroundworkReusableActivityStores>();
        lane.Alias<IActivityDefinitionLayoutStore, GroundworkReusableActivityStores>();
        lane.Alias<IActivityDraftValidationStore, GroundworkReusableActivityStores>();
        lane.Alias<IActivityForkStore, GroundworkReusableActivityStores>();
        lane.Alias<IActivityDirectDependencyStore, GroundworkReusableActivityStores>();
        lane.Alias<ICreateActivityDefinitionCommand, GroundworkReusableActivityStores>();
        lane.Alias<ISaveActivityForkCandidateCommand, GroundworkReusableActivityStores>();
        lane.Alias<IPruneActivityForkCandidatesCommand, GroundworkReusableActivityStores>();
        lane.Alias<IApplyActivityForkCandidateCommand, GroundworkReusableActivityStores>();
        lane.Alias<IUpdateActivityDefinitionPresentationCommand, GroundworkReusableActivityStores>();
        lane.Alias<ICreateActivityDraftCommand, GroundworkReusableActivityStores>();
        lane.Alias<IUpdateActivityDraftPresentationCommand, GroundworkReusableActivityStores>();
        lane.Alias<ICreateActivityDraftConflictCopyCommand, GroundworkReusableActivityStores>();
        lane.Alias<IReplaceActivityDraftCommand, GroundworkReusableActivityStores>();
        lane.Alias<IApplyActivityContractProposalCommand, GroundworkReusableActivityStores>();
        lane.Alias<IDiscardActivityDraftCommand, GroundworkReusableActivityStores>();
        lane.Alias<IStoreActivityDraftValidationCommand, GroundworkReusableActivityStores>();
        lane.Alias<IChangeActivityVersionLifecycleCommand, GroundworkReusableActivityStores>();
        lane.Alias<ISetActivityDefinitionRecommendationCommand, GroundworkReusableActivityStores>();

        lane.Alias<IActivityDependencyProjectionStore, GroundworkActivityDependencyProjection>();
        lane.Alias<IActivityDependencyProjectionRebuilder, GroundworkActivityDependencyProjection>();
        lane.Alias<IActivityUpgradePlanStore, GroundworkActivityUpgradePlanStore>();
        lane.Alias<IActivityUpgradeApplyReceiptStore, GroundworkActivityUpgradePlanStore>();

        services.TryAddScoped<IIdentityGenerator, ShortIdentityGenerator>();
        services.TryAddScoped<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
        services.TryAddScoped<IActivityDefinitionFactory, ActivityDefinitionFactory>();
        services.TryAddScoped<IActivityDefinitionVersionFactory, ActivityDefinitionVersionFactory>();

        return services;
    }
}
