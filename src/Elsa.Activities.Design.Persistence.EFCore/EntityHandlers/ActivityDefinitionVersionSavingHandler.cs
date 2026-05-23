using Elsa.Activities.Design.Persistence.Core.Entities;
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
            return ValueTask.CompletedTask;
        }
    }
}
