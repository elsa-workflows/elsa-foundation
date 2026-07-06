using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;

public sealed class WorkflowDefinitionDraftLoadingHandler(IPayloadSerializer payloadSerializer, ILogger<WorkflowDefinitionDraftLoadingHandler> logger)
    : StateSourceLoadingHandlerBase<WorkflowDefinitionDraft>(payloadSerializer, logger),
        IEntityLoadingHandler<WorkflowsDesignDbContext, WorkflowDefinitionDraft>
{
    public ValueTask Handle(WorkflowsDesignDbContext dbContext, WorkflowDefinitionDraft? entity, CancellationToken cancellationToken) =>
        Load(dbContext, entity);
}
