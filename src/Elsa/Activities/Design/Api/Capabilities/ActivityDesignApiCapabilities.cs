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
            new("activity-authoring-capabilities", "design/activities/authoring-capabilities"),
            new("activity-definitions", "design/activities/definitions"),
            new("activity-definition", "design/activities/definitions/{definitionId}", true),
            new("activity-definition-drafts", "design/activities/definitions/{definitionId}/drafts", true),
            new("activity-definition-draft", "design/activities/drafts/{draftId}", true),
            new("activity-definition-versions", "design/activities/definitions/{definitionId}/versions", true),
            new("activity-definition-version", "design/activities/versions/{versionId}", true),
            new("activity-version-diff", "design/activities/versions/{fromVersionId}/diff/{toVersionId}", true),
            new("activity-draft-conflict-copies", "design/activities/drafts/{draftId}/conflict-copies", true),
            new("activity-definition-recommendation", "design/activities/definitions/{definitionId}/recommendation", true),
            new("recommended-activity-definitions", "design/activities/definitions/picker"),
            new("activity-availability", "design/activities/availability/settings"),
            new("activity-availability-diagnostics", "design/activities/availability/diagnostics")
        ],
        SourceFeatureId);
}
