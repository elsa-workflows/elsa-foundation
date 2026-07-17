using Elsa.Api.Capabilities.Models;

namespace Elsa.Activities.Design.Api.Capabilities;

public static class ActivityDesignApiCapabilities
{
    public const string CapabilityId = "elsa.api.activity-design";
    public const string SourceFeatureId = "ActivitiesDesignApi";

    public static ApiCapabilityDeclaration StaticDeclaration { get; } = new(
        CapabilityId,
        1,
        [
            new("activity-catalog", "design/activities/catalog"),
            new("activity-definition-recommendation", "design/activities/definitions/{definitionId}/recommendation", true),
            new("recommended-activity-definitions", "design/activities/definitions/picker"),
            new("activity-availability", "design/activities/availability/settings"),
            new("activity-availability-diagnostics", "design/activities/availability/diagnostics")
        ],
        SourceFeatureId);
}
