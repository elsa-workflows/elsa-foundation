using Elsa.Persistence.EFCore.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;

namespace Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers
{
    public sealed class WorkflowDefinitionSavingHandler : IEntitySavingHandler<WorkflowsDesignDbContext, WorkflowDefinition>
    {
        public ValueTask Handle(WorkflowsDesignDbContext dbContext, WorkflowDefinition entity, CancellationToken cancellationToken)
        {
            dbContext.Entry(entity).Property(nameof(WorkflowMetadata.IsSystem)).CurrentValue = entity.MetaData?.IsSystem ?? false;
            return ValueTask.CompletedTask;
        }
    }
}
