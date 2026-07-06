using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;

namespace Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;

public sealed class WorkflowDefinitionVersionSavingHandler(IPayloadSerializer payloadSerializer)
    : StateSourceSavingHandlerBase<WorkflowDefinitionVersion>(payloadSerializer),
        IEntitySavingHandler<WorkflowsDesignDbContext, WorkflowDefinitionVersion>
{
    public ValueTask Handle(WorkflowsDesignDbContext dbContext, WorkflowDefinitionVersion entity, CancellationToken cancellationToken) =>
        Save(entity);
}
