using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Api.Projections;

/// <summary>
/// Explicit entity → API-view projections for the Activities design surface. Replaces the reflective
/// object-mapper: each projection is a plain function over the read contracts, composed where views
/// nest. Lives in the API layer (not on the DTO records) and is trivially unit-testable.
/// </summary>
public static class ActivityViewProjections
{
    public static ActivityDefinitionView ToView(this IActivityDefinition definition) =>
        new(definition.Id, definition.ActivityTypeKey, definition.Category, definition.DisplayName, definition.Description);

    public static ActivityDefinitionVersionDetailsView ToDetailsView(this IActivityDefinitionVersion version) =>
        new(
            version.Id,
            version.Version,
            version.DescriptorType,
            version.DescriptorPayload,
            version.Definition.ToView(),
            version.Inputs,
            version.Outputs,
            version.DesignFacets,
            version.ExecutionType);
}
