using Elsa.Api.Capabilities.Models;

namespace Elsa.Workflows.Publishing.Api.Capabilities;

public static class PublishingApiCapabilities
{
    public const string CapabilityId = "elsa.api.publishing";
    public const string SourceFeatureId = "WorkflowsPublishingApi";

    public static ApiCapabilityDeclaration StaticDeclaration { get; } = new(
        CapabilityId,
        1,
        [
            new("publication-preflight", "publishing/workflows/{versionId}/preflight", templated: true),
            new("workflow-publish", "publishing/workflows/{versionId}/publish", templated: true),
            new("publication-slots", "publishing/workflows/{definitionId}/slots", templated: true),
            new("publication-policy", "publishing/workflows/{definitionId}/policy", templated: true),
            new("workflow-test-runs", "publishing/workflows/{versionId}/test-runs", templated: true),
            new("workflow-draft-test-runs", "publishing/workflows/drafts/test-runs")
        ],
        SourceFeatureId);
}
