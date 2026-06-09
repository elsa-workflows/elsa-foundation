using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using System.Text.Json;

namespace Elsa.Activities.Design.Persistence.EFCore.EntityHandlers
{
    /// <summary>
    /// Entity-saving handler for <see cref="ActivityDefinitionVersion"/>. Serialises the rich
    /// projections into their <c>*Source</c> columns and writes the opaque descriptor payload string
    /// from the <c>DescriptorPayload</c> <see cref="JsonElement"/>. The design domain serialises on
    /// write and never deserialises to a concrete descriptor type — <c>DescriptorType</c> is set by
    /// the producer (reconciler / design API), not derived here.
    /// </summary>
    public sealed class ActivityDefinitionVersionSavingHandler(IPayloadSerializer payloadSerializer)
        : IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>
    {
        public ValueTask Handle(ActivitiesDesignDbContext dbContext, ActivityDefinitionVersion entity, CancellationToken cancellationToken)
        {
            entity.InputsSource = payloadSerializer.Serialize(entity.Inputs);
            entity.OutputsSource = payloadSerializer.Serialize(entity.Outputs);
            entity.PortsSource = payloadSerializer.Serialize(entity.Ports);

            // Persist the descriptor as opaque JSON. No type knowledge, no Kind derivation.
            entity.DescriptorPayloadSource = entity.DescriptorPayload.ValueKind == JsonValueKind.Undefined
                ? null
                : entity.DescriptorPayload.GetRawText();

            return ValueTask.CompletedTask;
        }
    }
}
