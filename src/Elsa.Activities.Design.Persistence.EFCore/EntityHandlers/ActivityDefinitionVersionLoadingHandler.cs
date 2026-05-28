using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Persistence.EFCore.EntityHandlers
{
    public sealed class ActivityDefinitionVersionLoadingHandler(
        IPayloadSerializer payloadSerializer,
        IImplementationDescriptorRegistry descriptorRegistry,
        ILogger<ActivityDefinitionVersionLoadingHandler> logger)
        : IEntityLoadingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>
    {
        public ValueTask Handle(ActivitiesDesignDbContext dbContext, ActivityDefinitionVersion? entity, CancellationToken cancellationToken)
        {
            if (entity == null)
                return ValueTask.CompletedTask;

            try
            {
                if (!string.IsNullOrWhiteSpace(entity.InputsSource))
                    entity.Inputs = payloadSerializer.Deserialize<IEnumerable<InputDefinition>>(entity.InputsSource);
                if (!string.IsNullOrWhiteSpace(entity.OutputsSource))
                    entity.Outputs = payloadSerializer.Deserialize<IEnumerable<OutputDefinition>>(entity.OutputsSource);
                if (!string.IsNullOrWhiteSpace(entity.PortsSource))
                    entity.Ports = payloadSerializer.Deserialize<IEnumerable<ActivityPortDefinition>>(entity.PortsSource);
            }
            catch (Exception exp)
            {
                logger.LogError(exp, "Could not deserialize activity version inputs/outputs/ports: {VersionId}. Reverting to default state", entity.Id);
            }

            if (!string.IsNullOrWhiteSpace(entity.ImplementationDescriptorPayload))
            {
                var descriptorType = descriptorRegistry.Resolve(entity.ImplementationKind)
                    ?? throw new ActivityDescriptorDeserialisationException(
                        entity.Id,
                        entity.ImplementationKind,
                        $"No implementation descriptor type registered for kind '{entity.ImplementationKind}' on version '{entity.Id}'. The owning module may not be installed.");

                try
                {
                    entity.ImplementationDescriptor = (IImplementationDescriptor)payloadSerializer.Deserialize(entity.ImplementationDescriptorPayload, descriptorType);
                }
                catch (Exception inner)
                {
                    throw new ActivityDescriptorDeserialisationException(
                        entity.Id,
                        entity.ImplementationKind,
                        $"Failed to deserialise implementation descriptor payload for version '{entity.Id}' (kind '{entity.ImplementationKind}', target type '{descriptorType.FullName}'). Persisted row may be corrupt or the descriptor type may have changed shape.",
                        inner);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
