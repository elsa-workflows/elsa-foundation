using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.EFCore
{
    public abstract class EFCoreWorkflowsPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<WorkflowsDesignDbContext>
    {
        protected override void OnConfiguring(IServiceCollection services)
        {
            if (UseCommands)
            {
                services.AddScoped<IEntitySavingHandler<WorkflowsDesignDbContext, WorkflowDefinitionVersion>, WorkflowDefinitionVersionSavingHandler>();
            }

            if (UseQueries)
            {
                services
                    .AddScoped<IEntityLoadingHandler<WorkflowsDesignDbContext, WorkflowDefinitionVersion>, WorkflowDefinitionVersionLoadingHandler>();
            }
        }
    }
}