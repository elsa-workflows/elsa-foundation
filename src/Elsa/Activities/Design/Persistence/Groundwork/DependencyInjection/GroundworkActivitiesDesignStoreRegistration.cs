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
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection;

/// <summary>
/// Registers the Groundwork (document) implementations of the activity-design read ports. A provider
/// feature is responsible for registering the concrete <see cref="Groundwork.Documents.Store.IDocumentStore"/>
/// (and the host's <see cref="Elsa.Serialization.Core.IPayloadSerializer"/>) these adapters consume; this
/// method only swaps the read-port contracts over to the document-backed implementations, mirroring the
/// runtime lane's <c>AddGroundworkRuntimeStores</c> and the workflows-design lane's
/// <c>AddGroundworkWorkflowsDesignStores</c>.
/// </summary>
public static class GroundworkActivitiesDesignStoreRegistration
{
    public static IServiceCollection AddGroundworkActivitiesDesignStores(this IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IGroundworkStorageManifestSource, ActivitiesDesignGroundworkStorageManifestSource>());

        services.RemoveAll<IActivityDefinitionStore>();
        services.AddScoped<IActivityDefinitionStore, GroundworkActivityDefinitionStore>();

        services.RemoveAll<IActivityDefinitionVersionStore>();
        services.AddScoped<IActivityDefinitionVersionStore, GroundworkActivityDefinitionVersionStore>();

        services.RemoveAll<IAddActivityDefinitionCommand>();
        services.AddScoped<IAddActivityDefinitionCommand, GroundworkAddActivityDefinitionCommand>();

        services.RemoveAll<IAddCommand<ActivityDefinitionVersion>>();
        services.AddScoped<IAddCommand<ActivityDefinitionVersion>, GroundworkAddActivityDefinitionVersionCommand>();

        services.RemoveAll<IActivityDefinitionLookup>();
        services.AddScoped<IActivityDefinitionLookup, ActivityDefinitionLookup>();

        services.RemoveAll<IActivityAvailabilitySettingsStore>();
        services.AddScoped<IActivityAvailabilitySettingsStore, GroundworkActivityAvailabilitySettingsStore>();

        services.AddScoped<GroundworkReusableActivityStores>();
        services.RemoveAll<IActivityDefinitionAuthoringStore>();
        services.AddScoped<IActivityDefinitionAuthoringStore>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IActivityDefinitionDraftStore>();
        services.AddScoped<IActivityDefinitionDraftStore>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IActivityDefinitionVersionPublicationStore>();
        services.AddScoped<IActivityDefinitionVersionPublicationStore>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IRecommendedActivityDefinitionPickerStore>();
        services.AddScoped<IRecommendedActivityDefinitionPickerStore>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IActivityDefinitionLayoutStore>();
        services.AddScoped<IActivityDefinitionLayoutStore>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IActivityDraftValidationStore>();
        services.AddScoped<IActivityDraftValidationStore>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IActivityDirectDependencyStore>();
        services.AddScoped<IActivityDirectDependencyStore>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.AddScoped<GroundworkActivityDependencyProjection>();
        services.RemoveAll<IActivityDependencyProjectionStore>();
        services.AddScoped<IActivityDependencyProjectionStore>(sp => sp.GetRequiredService<GroundworkActivityDependencyProjection>());
        services.RemoveAll<IActivityDependencyProjectionRebuilder>();
        services.AddScoped<IActivityDependencyProjectionRebuilder>(sp => sp.GetRequiredService<GroundworkActivityDependencyProjection>());
        services.RemoveAll<IActivityUpgradePlanStore>();
        services.AddScoped<IActivityUpgradePlanStore, GroundworkActivityUpgradePlanStore>();

        services.RemoveAll<ICreateActivityDefinitionCommand>();
        services.AddScoped<ICreateActivityDefinitionCommand>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IUpdateActivityDefinitionPresentationCommand>();
        services.AddScoped<IUpdateActivityDefinitionPresentationCommand>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<ICreateActivityDraftCommand>();
        services.AddScoped<ICreateActivityDraftCommand>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IReplaceActivityDraftCommand>();
        services.AddScoped<IReplaceActivityDraftCommand>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IDiscardActivityDraftCommand>();
        services.AddScoped<IDiscardActivityDraftCommand>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IStoreActivityDraftValidationCommand>();
        services.AddScoped<IStoreActivityDraftValidationCommand>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<IChangeActivityVersionLifecycleCommand>();
        services.AddScoped<IChangeActivityVersionLifecycleCommand>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());
        services.RemoveAll<ISetActivityDefinitionRecommendationCommand>();
        services.AddScoped<ISetActivityDefinitionRecommendationCommand>(sp => sp.GetRequiredService<GroundworkReusableActivityStores>());

        services.TryAddScoped<IIdentityGenerator, ShortIdentityGenerator>();
        services.TryAddScoped<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
        services.TryAddScoped<IActivityDefinitionFactory, ActivityDefinitionFactory>();
        services.TryAddScoped<IActivityDefinitionVersionFactory, ActivityDefinitionVersionFactory>();

        return services;
    }
}
