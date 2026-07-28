using Elsa.Mediator.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class PublishActivityDraftRequestHandler(IActivityDefinitionPublisher publisher)
    : IRequestHandler<PublishActivityDraft, ActivityPublicationReceiptView>
{
    public async Task<ActivityPublicationReceiptView> Handle(
        PublishActivityDraft request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DraftId) || request.ExpectedDraftRevision <= 0 ||
            string.IsNullOrWhiteSpace(request.Version) ||
            string.IsNullOrWhiteSpace(request.ReviewToken) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.ExpectedDefinitionHeadVersionId is not null && string.IsNullOrWhiteSpace(request.ExpectedDefinitionHeadVersionId))
            throw new ActivityPublicationRejectedException(
                "activity.request.invalid",
                "The publication request is malformed.",
                []);
        if (!SemVer.TryParse(request.Version, out _))
            throw new ActivityPublicationRejectedException(
                "activity.request.invalid",
                "The publication version must be valid SemVer 2.0.0 syntax.",
                [new(
                    "activity.version.invalid",
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
        return ActivityPublicationReceiptView.From(result);
    }
}
