using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Core;
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

        services.TryAddScoped<IIdentityGenerator, ShortIdentityGenerator>();
        services.TryAddScoped<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
        services.TryAddScoped<IActivityDefinitionFactory, ActivityDefinitionFactory>();
        services.TryAddScoped<IActivityDefinitionVersionFactory, ActivityDefinitionVersionFactory>();

        return services;
    }
}
