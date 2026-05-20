using Elsa.Persistence.EFCore;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Persistence.EFCore
{
    public abstract class EFCoreWorkflowsPersistenceFeatureBase : PersistenceShellFeatureBase<WorkflowDefinitionDbContext>
    {
        protected override void OnConfiguring(IServiceCollection services)
        {
            if (UseCommands)
            {
                services.AddScoped<IWorkflowDefinitionDeleteCommand, EFCoreWorkflowDefinitionDeleteCommand>();
                services.AddScoped<IEntitySavingHandler<WorkflowDefinitionDbContext, WorkflowDefinition>, WorkflowDefinitionSavingHandler>();
            }

            if (UseQueries)
            {
                services
                    .AddScoped<IEntityLoadingHandler<WorkflowDefinitionDbContext, WorkflowDefinition>, WorkflowDefinitionLoadingHandler>()
                    .AddScoped<IWorkflowDefinitionQueries, EFCoreWorkflowDefinitionQueries>();
            }
        }
    }
}