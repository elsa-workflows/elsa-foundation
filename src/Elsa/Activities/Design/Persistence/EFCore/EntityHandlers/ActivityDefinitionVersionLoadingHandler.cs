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
            try
            {
                // Rehydrate through the canonical payload serializer (the rule: all domain-payload JSON
                // goes through IPayloadSerializer). Clone so the element is self-contained. Guarded like
                // its inputs/outputs/facets siblings so a malformed descriptor soft-fails to the default
                // (Undefined) rather than aborting entity loading (issue #417 item 7). Kept in its own
                // try so a bad descriptor doesn't discard successfully-parsed projections, and vice versa.
                entity.DescriptorPayload = payloadSerializer.Deserialize<JsonElement>(entity.DescriptorPayloadSource).Clone();
            }
            catch (Exception exp)
            {
                logger.LogError(exp, "Could not deserialize activity version descriptor payload: {VersionId}. Reverting to default state", entity.Id);
            }
        }

        return ValueTask.CompletedTask;
    }
}
