using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.EFCore.EntityHandlers;

/// <summary>
/// Shared entity-saving behaviour for the design entities that carry a serialized
/// <see cref="Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState"/>
/// (<see cref="WorkflowDefinitionDraft"/> and <see cref="WorkflowDefinitionVersion"/>). Serializes
/// <see cref="IStateSourcedEntity.State"/> into <see cref="IStateSourcedEntity.StateSource"/>, writing
/// the empty string when there is no state. Extracted from the two byte-for-byte-identical concrete
/// handlers (issue #417 item 5).
/// </summary>
/// <remarks>
/// Deliberately does NOT implement <c>IEntitySavingHandler&lt;,&gt;</c> itself: the concrete sealed
/// handlers implement the interface and delegate to <see cref="Save"/>. See
/// <see cref="StateSourceLoadingHandlerBase{TEntity}"/> for the scan-registration rationale.
/// </remarks>
public abstract class StateSourceSavingHandlerBase<TEntity>(IPayloadSerializer payloadSerializer)
    where TEntity : Entity, IStateSourcedEntity
{
    protected ValueTask Save(TEntity entity)
    {
        var stateSource = string.Empty;
        if (entity.State is not null)
        {
            stateSource = payloadSerializer.Serialize(entity.State);
        }

        entity.StateSource = stateSource;

        return ValueTask.CompletedTask;
    }
}
