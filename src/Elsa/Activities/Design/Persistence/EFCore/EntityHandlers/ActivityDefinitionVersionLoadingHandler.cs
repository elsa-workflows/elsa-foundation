using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Elsa.Activities.Design.Persistence.EFCore.EntityHandlers;

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
            if (!string.IsNullOrWhiteSpace(entity.DesignFacetsSource))
                entity.DesignFacets = payloadSerializer.Deserialize<IEnumerable<ActivityDesignFacet>>(entity.DesignFacetsSource);
        }
        catch (Exception exp)
        {
            logger.LogError(exp, "Could not deserialize activity version inputs/outputs/design facets: {VersionId}. Reverting to default state", entity.Id);
        }

        if (!string.IsNullOrWhiteSpace(entity.DescriptorPayloadSource))
        {
            // Rehydrate through the canonical payload serializer (the rule: all domain-payload JSON
            // goes through IPayloadSerializer). Clone so the element is self-contained.
            entity.DescriptorPayload = payloadSerializer.Deserialize<JsonElement>(entity.DescriptorPayloadSource).Clone();
        }

        return ValueTask.CompletedTask;
    }
}
