using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Elsa.Activities.Design.Persistence.EFCore.EntityHandlers
{
    /// <summary>
    /// Entity-loading handler for <see cref="ActivityDefinitionVersion"/>. Rehydrates the rich
    /// input/output/port projections and parses the opaque descriptor payload string into a
    /// <see cref="JsonElement"/>. It does NOT resolve a descriptor CLR type — the design domain never
    /// materialises the descriptor; only the runtime feature that owns the descriptor type does.
    /// </summary>
    public sealed class ActivityDefinitionVersionLoadingHandler(
        IPayloadSerializer payloadSerializer,
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

            if (!string.IsNullOrWhiteSpace(entity.DescriptorPayloadSource))
            {
                // Parse to an independent JsonElement (Clone so it outlives the JsonDocument).
                using var document = JsonDocument.Parse(entity.DescriptorPayloadSource);
                entity.DescriptorPayload = document.RootElement.Clone();
            }

            return ValueTask.CompletedTask;
        }
    }
}
