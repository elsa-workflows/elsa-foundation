using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;

public sealed class WorkflowDefinitionDraftLoadingHandler(IPayloadSerializer payloadSerializer, ILogger<WorkflowDefinitionDraftLoadingHandler> logger)
    : IEntityLoadingHandler<WorkflowsDesignDbContext, WorkflowDefinitionDraft>
{
    public ValueTask Handle(WorkflowsDesignDbContext dbContext, WorkflowDefinitionDraft? entity, CancellationToken cancellationToken)
    {
        if (entity == null)
            return ValueTask.CompletedTask;

        var stateSourceProperty = dbContext
            .Entry(entity)
            .Property(nameof(WorkflowDefinitionDraft.StateSource));

        var stateSource = (string?)stateSourceProperty.CurrentValue;

        if (string.IsNullOrWhiteSpace(stateSource))
        {
            entity.State = WorkflowDefinitionState.Empty;
            return ValueTask.CompletedTask;
        }

        try
        {
            entity.State = payloadSerializer.Deserialize<WorkflowDefinitionState>(stateSource);
        }
        catch (Exception exp)
        {
            logger.LogError(exp, "Could not deserialize workflow definition state: {DefinitionId}. Reverting to default state", entity.Id);
            entity.State = WorkflowDefinitionState.Empty;
        }

        return ValueTask.CompletedTask;
    }
}