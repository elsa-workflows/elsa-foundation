using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Models;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services
{
    public sealed class WorkflowDefinitionSavingHandler(IPayloadSerializer payloadSerializer) : IEntitySavingHandler<WorkflowDefinitionDbContext, WorkflowDefinition>
    {
        public ValueTask Handle(WorkflowDefinitionDbContext dbContext, WorkflowDefinition entity, CancellationToken cancellationToken)
        {
            var data = new WorkflowDefinitionState(entity.Options, entity.Variables, entity.Inputs, entity.Outputs, entity.Outcomes, entity.CustomProperties);
            var json = payloadSerializer.Serialize(data);

            dbContext.Entry(entity).Property("Data").CurrentValue = json;
            dbContext.Entry(entity).Property("UsableAsActivity").CurrentValue = data.Options.UsableAsActivity;

            return ValueTask.CompletedTask;
        }
    }
}
