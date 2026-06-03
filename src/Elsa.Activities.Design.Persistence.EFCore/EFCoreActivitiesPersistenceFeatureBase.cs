using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Persistence.EFCore.Services;
using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Persistence.EFCore;

public abstract class EFCoreActivitiesPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<ActivitiesDesignDbContext>
{
    protected override void OnBeforeConfiguring(IServiceCollection services)
    {
        if (UseCommands)
        {
            services
                .AddScoped<IAddActivityDefinitionCommand, AddActivityDefinitionCommand>()
                // ActivityDefinitionVersionSavingHandler is a typed IEntitySavingHandler<,>; the
                // assembly scan registers it and the single ApplyEntitySavingHandlers aggregator
                // (registered by the EF Core base feature) dispatches it when OnEntitySaving fires.
                .AddEntitySavingHandlersFrom(GetType().Assembly)
                .AddEntitySavingHandlersFrom(typeof(EFCoreActivitiesPersistenceFeatureBase).Assembly);
        }

        if (UseQueries)
        {
            services.AddScoped<IActivityDefinitionLookup, ActivityDefinitionLookup>();
        }
    }
}
