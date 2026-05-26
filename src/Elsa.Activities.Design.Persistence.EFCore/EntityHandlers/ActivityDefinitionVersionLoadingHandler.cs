using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Persistence.EFCore.EntityHandlers
{
    public sealed class ActivityDefinitionVersionLoadingHandler(IPayloadSerializer payloadSerializer, ILogger<ActivityDefinitionVersionLoadingHandler> logger)
        : IEntityLoadingHandler<ActivitiesDesignDbContext, ActivityDefinitionVersion>
    {
        public ValueTask Handle(ActivitiesDesignDbContext dbContext, ActivityDefinitionVersion? entity, CancellationToken cancellationToken)
        {
            if (entity == null)
                return ValueTask.CompletedTask;

            var inputsSourceProperty = dbContext
                .Entry(entity)
                .Property(nameof(ActivityDefinitionVersion.InputsSource));
            var outputsSourceProperty = dbContext
                .Entry(entity)
                .Property(nameof(ActivityDefinitionVersion.OutputsSource));
            var portsSourceProperty = dbContext
                .Entry(entity)
                .Property(nameof(ActivityDefinitionVersion.PortsSource));

            var inputsSource = (string?)inputsSourceProperty.CurrentValue;
            var outputsSource = (string?)outputsSourceProperty.CurrentValue;
            var portsSource = (string?)portsSourceProperty.CurrentValue;

            try
            {
                if (!string.IsNullOrWhiteSpace(inputsSource))
                {
                    entity.Inputs = payloadSerializer.Deserialize<IEnumerable<InputDefinition>>(inputsSource);
                }
                if (!string.IsNullOrWhiteSpace(outputsSource))
                {
                    entity.Outputs = payloadSerializer.Deserialize<IEnumerable<OutputDefinition>>(outputsSource);
                }
                if (!string.IsNullOrWhiteSpace(portsSource))
                {
                    entity.Ports = payloadSerializer.Deserialize<IEnumerable<ActivityPortDefinition>>(portsSource);
                }
            }
            catch (Exception exp)
            {
                logger.LogError(exp, "Could not deserialize activity version state: {VersionId}. Reverting to default state", entity.Id);
            }

            return ValueTask.CompletedTask;
        }
    }
}
