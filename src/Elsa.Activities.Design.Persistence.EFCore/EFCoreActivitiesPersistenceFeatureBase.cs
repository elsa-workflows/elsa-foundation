using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Activities.Design.Persistence.EFCore.EntityHandlers;
using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Persistence.EFCore
{
    public abstract class EFCoreActivitiesPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<ActivitiesDesignDbContext>
    {
        protected override void OnConfiguring(IServiceCollection services)
        {
            if (UseCommands)
            {
                services.AddScoped<IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>, ActivityDefinitionVersionSavingHandler>();
            }

            if (UseQueries)
            {
                services
                    .AddScoped<IEntityLoadingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>, ActivityDefinitionVersionLoadingHandler>();
            }
        }
    }
}