using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Persistence.EFCore.EntityHandlers
{
    /// <summary>
    /// Entity-saving handler for <see cref="ActivityDefinitionVersion"/>. Serialises the rich
    /// projections into their <c>*Source</c> columns and derives the implementation kind +
    /// descriptor payload before the row is flushed. Dispatched by the single
    /// <c>ApplyEntitySavingHandlers</c> aggregator when <c>OnEntitySaving</c> is published — the
    /// typed interface does the entity-type filtering, so no inline <c>is</c> check is needed.
    /// </summary>
    public sealed class ActivityDefinitionVersionSavingHandler(IPayloadSerializer payloadSerializer)
        : IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>
    {
        public ValueTask Handle(ActivitiesDesignDbContext dbContext, ActivityDefinitionVersion entity, CancellationToken cancellationToken)
        {
            entity.InputsSource = payloadSerializer.Serialize(entity.Inputs);
            entity.OutputsSource = payloadSerializer.Serialize(entity.Outputs);
            entity.PortsSource = payloadSerializer.Serialize(entity.Ports);

            // Derive the kind column from the descriptor itself (single source of truth)
            // and serialise the descriptor into the real payload property — post-insert
            // immutability is enforced via PropertySaveBehavior.Throw in the entity configuration.
            entity.ImplementationKind = entity.ImplementationDescriptor.Kind;
            entity.ImplementationDescriptorPayload = payloadSerializer.Serialize(entity.ImplementationDescriptor);

            return ValueTask.CompletedTask;
        }
    }
}
