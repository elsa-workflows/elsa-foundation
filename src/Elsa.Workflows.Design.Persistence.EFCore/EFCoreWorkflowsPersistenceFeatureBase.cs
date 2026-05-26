using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.EFCore
{
    public abstract class EFCoreWorkflowsPersistenceFeatureBase : EFCorePersistenceShellFeatureBase<WorkflowsDesignDbContext>
    {
        protected override void OnAfterConfigured(IServiceCollection services)
        {
            if (UseCommands)
            {
                services
                    .AddScoped<IAddWorkflowDefinitionCommand, AddWorkflowDefinitionCommand>()
                    .AddEntitySavingHandlersFrom(GetType().Assembly)
                    .AddEntitySavingHandlersFrom(typeof(EFCoreWorkflowsPersistenceFeatureBase).Assembly);
            }

            if (UseQueries)
            {
                services
                    .AddScoped<IWorkflowDefinitionLookup, WorkflowDefinitionLookup>()
                    .AddEntityLoadingHandlersFrom(GetType().Assembly)
                    .AddEntityLoadingHandlersFrom(typeof(EFCoreWorkflowsPersistenceFeatureBase).Assembly);
            }
        }
    }
}