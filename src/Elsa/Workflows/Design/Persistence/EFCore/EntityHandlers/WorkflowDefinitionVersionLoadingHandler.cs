using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;

public sealed class WorkflowDefinitionVersionLoadingHandler(IPayloadSerializer payloadSerializer, ILogger<WorkflowDefinitionVersionLoadingHandler> logger)
    : StateSourceLoadingHandlerBase<WorkflowDefinitionVersion>(payloadSerializer, logger),
        IEntityLoadingHandler<WorkflowsDesignDbContext, WorkflowDefinitionVersion>
{
    public ValueTask Handle(WorkflowsDesignDbContext dbContext, WorkflowDefinitionVersion? entity, CancellationToken cancellationToken) =>
        Load(dbContext, entity);
}
