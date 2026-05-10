using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core;
using Elsa.Persistence.EFCore;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Services;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Options;

namespace Elsa.Workflows.Design.Persistence.EFCore
{    
    public abstract class EFCoreWorkflowsPersistenceFeatureBase : PersistenceShellFeatureBase<WorkflowDefinitionDbContext>
    {        
        protected override void OnConfiguring(IServiceCollection services)
        {
            if(Commands.IsCommandSupported<WorkflowDefinition>(CommandType.Delete))
            {
                services.AddScoped<IWorkflowDefinitionDeleteCommand, EFCoreWorkflowDefinitionDeleteCommand>();
            }
            if (Commands.IsAnyCommandSupported<WorkflowDefinition>([CommandType.Add, CommandType.Save, CommandType.BulkInsert, CommandType.BulkUpsert]))
            {
                services.AddScoped<IEntitySavingHandler<WorkflowDefinitionDbContext, WorkflowDefinition>, WorkflowDefinitionSavingHandler>();
            }

            services                                
                .AddScoped<IEntityLoadingHandler<WorkflowDefinitionDbContext, WorkflowDefinition>, WorkflowDefinitionLoadingHandler>()                
                .AddScoped<IWorkflowDefinitionQueries, EFCoreWorkflowDefinitionQueries>();
        }
    }
}