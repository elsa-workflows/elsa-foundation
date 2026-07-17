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
            new("publication-snapshot-preflight", "publishing/workflows/preflight"),
            new("publication-preflight", "publishing/workflows/{versionId}/preflight", templated: true),
            new("activity-publication-preflight", "design/activities/drafts/{draftId}/publication-preflight", templated: true),
            new("activity-publication", "design/activities/drafts/{draftId}/publish", templated: true),
            new("activity-publication-receipt", "design/activities/publications/{idempotencyKey}", templated: true),
            new("workflow-publish", "publishing/workflows/{versionId}/publish", templated: true),
            new("publication-slots", "publishing/workflows/{definitionId}/slots", templated: true),
            new("publication-policy", "publishing/workflows/{definitionId}/policy", templated: true),
            new("workflow-test-runs", "publishing/workflows/{versionId}/test-runs", templated: true),
            new("workflow-draft-test-runs", "publishing/workflows/drafts/test-runs"),
            new("activity-draft-test-run-dispatch", "publishing/activity-drafts/{draftId}/test-runs", templated: true),
            new("activity-draft-test-run-status", "publishing/activity-test-runs/{testRunId}", templated: true),
            new("activity-draft-test-run-idempotency-status", "publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}", templated: true),
            new("activity-draft-test-run-cancel", "publishing/activity-test-runs/{testRunId}/cancel", templated: true)
        ],
        SourceFeatureId);
}
