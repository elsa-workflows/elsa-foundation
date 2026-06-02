using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.EFCore.Events;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Persistence.EFCore.EntityHandlers
{
    /// <summary>
    /// Domain-event-driven entity-saving handler for <see cref="ActivityDefinitionVersion"/>.
    /// Migrated from the legacy <c>IEntitySavingHandler&lt;TDbContext,TEntity&gt;</c> mechanism
    /// to the canonical §2.6.1 domain-event dispatch. Filters by entity type inline; other
    /// features' handlers may subscribe to the same event independently.
    /// </summary>
    public sealed class ActivityDefinitionVersionSavingHandler(IPayloadSerializer payloadSerializer)
        : IEventHandler<OnEntitySaving>
    {
        public Task Handle(OnEntitySaving domainEvent, CancellationToken cancellationToken)
        {
            if (domainEvent.Entry.Entity is not ActivityDefinitionVersion entity)
                return Task.CompletedTask;
                        
            entity.InputsSource = payloadSerializer.Serialize(entity.Inputs);
            entity.OutputsSource = payloadSerializer.Serialize(entity.Outputs);
            entity.PortsSource = payloadSerializer.Serialize(entity.Ports);

            // Derive the kind column from the descriptor itself (single source of truth)
            // and serialise the descriptor into the real payload property — the
            // [Immutable] scanner enforces post-insert read-only at the central level.
            entity.ImplementationKind = entity.ImplementationDescriptor.Kind;
            entity.ImplementationDescriptorPayload = payloadSerializer.Serialize(entity.ImplementationDescriptor);

            return Task.CompletedTask;
        }
    }
}
