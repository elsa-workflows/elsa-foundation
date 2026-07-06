using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;

/// <summary>
/// Shared entity-loading behaviour for the design entities that carry a serialized
/// <see cref="WorkflowDefinitionState"/> (<see cref="WorkflowDefinitionDraft"/> and
/// <see cref="WorkflowDefinitionVersion"/>). Reads the <c>StateSource</c> shadow value and
/// deserializes it into <see cref="IStateSourcedEntity.State"/>, soft-failing to
/// <see cref="WorkflowDefinitionState.Empty"/> on a deserialization error. Extracted from the two
/// byte-for-byte-identical concrete handlers (issue #417 item 5).
/// </summary>
/// <remarks>
/// Deliberately does NOT implement <c>IEntityLoadingHandler&lt;,&gt;</c> itself: the concrete sealed
/// handlers implement the interface and delegate to <see cref="Load"/>. The assembly-scan
/// registration (<c>AddEntityLoadingHandlersFrom</c>) has no <c>IsAbstract</c>/open-generic guard, so
/// an abstract base implementing the closed interface would be picked up and fail container build.
/// </remarks>
public abstract class StateSourceLoadingHandlerBase<TEntity>(IPayloadSerializer payloadSerializer, ILogger logger)
    where TEntity : Entity, IStateSourcedEntity
{
    protected ValueTask Load(WorkflowsDesignDbContext dbContext, TEntity? entity)
    {
        if (entity == null)
            return ValueTask.CompletedTask;

        var stateSourceProperty = dbContext
            .Entry(entity)
            .Property(nameof(IStateSourcedEntity.StateSource));

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
