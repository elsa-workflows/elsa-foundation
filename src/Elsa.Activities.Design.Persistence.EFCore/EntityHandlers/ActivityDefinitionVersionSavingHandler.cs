using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.Configurations;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Persistence.EFCore.EntityHandlers
{
    public sealed class ActivityDefinitionVersionSavingHandler(IPayloadSerializer payloadSerializer) : IEntitySavingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>
    {
        public ValueTask Handle(ActivitiesDesignDbContext dbContext, ActivityDefinitionVersion entity, CancellationToken cancellationToken)
        {
            entity.InputsSource = payloadSerializer.Serialize(entity.Inputs);
            entity.OutputsSource = payloadSerializer.Serialize(entity.Outputs);
            entity.PortsSource = payloadSerializer.Serialize(entity.Ports);

            // Derive the kind column from the descriptor itself (single source of truth)
            // and serialise the descriptor into the shadow column.
            entity.ImplementationKind = entity.ImplementationDescriptor.Kind;
            var descriptorJson = payloadSerializer.Serialize(entity.ImplementationDescriptor);
            dbContext.Entry(entity)
                .Property(ActivityDefinitionVersionConfiguration.DescriptorShadowName)
                .CurrentValue = descriptorJson;

            return ValueTask.CompletedTask;
        }
    }
}
