using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Persistence.EFCore.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services
{
    public sealed class WorkflowDefinitionLoadingHandler(IPayloadSerializer payloadSerializer, ILogger<WorkflowDefinitionLoadingHandler> logger)
        : IEntityLoadingHandler<WorkflowDefinitionDbContext, WorkflowDefinition>
    {
        public ValueTask Handle(WorkflowDefinitionDbContext dbContext, WorkflowDefinition? entity, CancellationToken cancellationToken)
        {
            if (entity == null)
                return ValueTask.CompletedTask;

            var data = new WorkflowDefinitionState(entity.Options, entity.Variables, entity.Inputs, entity.Outputs, entity.Outcomes, entity.CustomProperties);
            var json = (string?)dbContext.Entry(entity).Property("Data").CurrentValue;

            try
            {
                if (!string.IsNullOrWhiteSpace(json))
                    data = payloadSerializer.Deserialize<WorkflowDefinitionState>(json);
            }
            catch (Exception exp)
            {
                logger.LogError(exp, "Could not deserialize workflow definition state: {DefinitionId}. Reverting to default state", entity.DefinitionId);
            }

            entity.Options = data.Options;
            entity.Variables = data.Variables;
            entity.Inputs = data.Inputs;
            entity.Outputs = data.Outputs;
            entity.Outcomes = data.Outcomes;
            entity.CustomProperties = data.CustomProperties;

            return ValueTask.CompletedTask;
        }
    }
}
