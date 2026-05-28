using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.Configurations;
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

            var entry = dbContext.Entry(entity);

            var inputsSource = (string?)entry.Property(nameof(ActivityDefinitionVersion.InputsSource)).CurrentValue;
            var outputsSource = (string?)entry.Property(nameof(ActivityDefinitionVersion.OutputsSource)).CurrentValue;
            var portsSource = (string?)entry.Property(nameof(ActivityDefinitionVersion.PortsSource)).CurrentValue;
            var descriptorJson = (string?)entry.Property(ActivityDefinitionVersionConfiguration.DescriptorShadowName).CurrentValue;

            try
            {
                if (!string.IsNullOrWhiteSpace(inputsSource))
                    entity.Inputs = payloadSerializer.Deserialize<IEnumerable<InputDefinition>>(inputsSource);
                if (!string.IsNullOrWhiteSpace(outputsSource))
                    entity.Outputs = payloadSerializer.Deserialize<IEnumerable<OutputDefinition>>(outputsSource);
                if (!string.IsNullOrWhiteSpace(portsSource))
                    entity.Ports = payloadSerializer.Deserialize<IEnumerable<ActivityPortDefinition>>(portsSource);
            }
            catch (Exception exp)
            {
                logger.LogError(exp, "Could not deserialize activity version inputs/outputs/ports: {VersionId}. Reverting to default state", entity.Id);
            }

            if (!string.IsNullOrWhiteSpace(descriptorJson))
            {
                var descriptorType = descriptorRegistry.Resolve(entity.ImplementationKind)
                    ?? throw new InvalidOperationException(
                        $"No implementation descriptor type registered for kind '{entity.ImplementationKind}' on version '{entity.Id}'. The owning module may not be installed.");

                entity.ImplementationDescriptor = (IImplementationDescriptor)payloadSerializer.Deserialize(descriptorJson, descriptorType);
            }

            return ValueTask.CompletedTask;
        }
    }
}
