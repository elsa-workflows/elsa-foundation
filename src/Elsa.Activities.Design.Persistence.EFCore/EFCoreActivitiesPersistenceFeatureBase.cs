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
                .AddEntitySavingHandlersFrom(GetType().Assembly)
                .AddEntitySavingHandlersFrom(typeof(EFCoreActivitiesPersistenceFeatureBase).Assembly);
        }

        if (UseQueries)
        {
            services
                .AddScoped<IActivityDefinitionLookup, ActivityDefinitionLookup>()
                .AddEntityLoadingHandlersFrom(GetType().Assembly)
                .AddEntityLoadingHandlersFrom(typeof(EFCoreActivitiesPersistenceFeatureBase).Assembly);
        }
    }
}
