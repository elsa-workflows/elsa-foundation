using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;

namespace Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;

public sealed class WorkflowDefinitionDraftSavingHandler(IPayloadSerializer payloadSerializer)
    : StateSourceSavingHandlerBase<WorkflowDefinitionDraft>(payloadSerializer),
        IEntitySavingHandler<WorkflowsDesignDbContext, WorkflowDefinitionDraft>
{
    public ValueTask Handle(WorkflowsDesignDbContext dbContext, WorkflowDefinitionDraft entity, CancellationToken cancellationToken) =>
        Save(entity);
}
