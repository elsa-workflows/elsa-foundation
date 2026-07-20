using System.Reflection;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Persistence.EFCore.Services;
using Elsa.Persistence.EFCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Persistence.EFCore;

public abstract class EFCoreActivitiesPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<ActivitiesDesignDbContext>
{
    // The saving/loading handlers (e.g. ActivityDefinitionVersion{Saving,Loading}Handler) live in
    // THIS assembly. The base registers both directions from this list, so neither can be forgotten.
    protected override IEnumerable<Assembly> EntityHandlerAssemblies =>
        [GetType().Assembly, typeof(EFCoreActivitiesPersistenceFeatureBase).Assembly];

    protected override void OnBeforeConfiguring(IServiceCollection services)
    {
        // Entity construction: the hasher + the factories that build read-models for new
        // definitions/versions (generating Id + content Hash). Available to every consumer
        // (reconciliation, the design API, Elsa-3 import) wherever persistence is composed.
        services.AddScoped<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
        services.AddScoped<IActivityDefinitionFactory, ActivityDefinitionFactory>();
        services.AddScoped<IActivityDefinitionVersionFactory, ActivityDefinitionVersionFactory>();

        if (UseCommands)
        {
            services.AddScoped<IAddActivityDefinitionCommand, AddActivityDefinitionCommand>();
            services.AddScoped<IAddActivityDefinitionVersionCommand, AddActivityDefinitionVersionCommand>();
        }

        if (UseQueries)
        {
            services.AddScoped<IActivityDefinitionStore, EFCoreActivityDefinitionStore>();
            services.AddScoped<IActivityDefinitionVersionStore, EFCoreActivityDefinitionVersionStore>();
            services.AddScoped<IActivityDefinitionLookup, ActivityDefinitionLookup>();
        }
    }
}
