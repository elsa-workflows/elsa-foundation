using Elsa.Activities.Design.Core.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Primitives.Diagnostics;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Workflows.Publishing.Api.Endpoints.ActivityDrafts.Publish;

[Post("/design/activities/drafts/{draftId}/publish")]
[RequirePermission(WorkflowPublishingPermissions.Manage)]
public sealed class Endpoint(IActivityDefinitionPublisher publisher) : ApiEndpoint<PublishActivityDraft, ActivityPublicationReceiptView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "PublishActivityDraftEndpoint";
        options.Accepts = ["application/json"];
        // A literal null body publishes with route-supplied identity alone, as the published contract allows.
        options.BodyMode = EndpointBodyMode.OptionalWithContentType;
        options.SuccessStatus = StatusCodes.Status201Created;
        options.DocumentedStatus = StatusCodes.Status200OK;
        options.Convention(builder => builder.WithMetadata(
            new ActivityProblemEndpointMetadata("Activity publication was rejected")));
    }

    public override async Task<ActivityPublicationReceiptView> HandleAsync(PublishActivityDraft request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DraftId) || request.ExpectedDraftRevision <= 0 ||
            string.IsNullOrWhiteSpace(request.Version) ||
            string.IsNullOrWhiteSpace(request.ReviewToken) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.ExpectedDefinitionHeadVersionId is not null && string.IsNullOrWhiteSpace(request.ExpectedDefinitionHeadVersionId))
            throw new ActivityPublicationRejectedException(
                ActivityErrorCodes.RequestInvalid,
                "The publication request is malformed.",
                []);
        if (!SemVer.TryParse(request.Version, out _))
            throw new ActivityPublicationRejectedException(
                ActivityErrorCodes.RequestInvalid,
                "The publication version must be valid SemVer 2.0.0 syntax.",
                [new(
                    ActivityErrorCodes.VersionInvalid,
                    ActivityDiagnosticSeverity.Error,
                    $"Version '{request.Version}' is not valid SemVer 2.0.0.",
                    new("ActivityDraft", request.DraftId, Revision: request.ExpectedDraftRevision),
                    Remediation: "Supply a valid semantic version.",
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal))]);

        var result = await publisher.PublishReviewedAsync(new(
            request.DraftId,
            request.ExpectedDraftRevision,
            request.ExpectedDefinitionHeadVersionId,
            request.Version,
            request.ReviewToken,
            request.IdempotencyKey), cancellationToken);
        var response = ActivityPublicationReceiptView.From(result);
        HttpContext.Response.Headers.Location = $"/design/activities/publications/{Uri.EscapeDataString(response.IdempotencyKey)}";
        return response;
    }
}
