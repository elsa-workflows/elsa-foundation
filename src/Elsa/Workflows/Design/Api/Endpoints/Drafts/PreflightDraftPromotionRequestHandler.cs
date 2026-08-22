using Elsa.Events.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts;

public sealed class PreflightDraftPromotionRequestHandler(
    IWorkflowDefinitionDraftStore draftStore,
    IWorkflowDefinitionVersionStore versionStore,
    IInlineEventPublisher inlineEventPublisher)
    : IRequestHandler<PreflightDraftPromotion, PromotionPreflightAssessmentView>
{
    public async Task<PromotionPreflightAssessmentView> Handle(
        PreflightDraftPromotion request,
        CancellationToken cancellationToken)
    {
        var draft = await draftStore.FindByIdAsync(request.DraftId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), request.DraftId);
        var errors = await inlineEventPublisher.TryDeriveValidationErrorsAsync(draft, cancellationToken);
        var latest = await versionStore.FindLatestVersionAsync(draft.WorkflowDefinitionId, cancellationToken);
        var initialAssessment = WorkflowVersionNumbering.AssessPromotion(
            latest?.Version,
            request.RequestedVersion,
            versionIdentityExists: false);
        var candidateIdentitySortKey = WorkflowVersionNumbering.GetCandidateIdentitySortKey(initialAssessment);
        var identityExists = candidateIdentitySortKey is not null &&
                             await versionStore.ExistsAsync(
                                 draft.WorkflowDefinitionId,
                                 candidateIdentitySortKey,
                                 cancellationToken);
        var assessment = WorkflowVersionNumbering.AssessPromotion(
            latest?.Version,
            request.RequestedVersion,
            identityExists);
        var issues = errors
            .Select(error => new PromotionPreflightIssueView("draft-validation", error.Message, error.Path))
            .Concat(assessment.Issues.Select(issue => new PromotionPreflightIssueView(issue.Code, issue.Message)))
            .ToArray();
        return new PromotionPreflightAssessmentView(
            errors.Count == 0 && assessment.IsReady,
            assessment.AssignmentMode,
            assessment.RequestedVersion,
            assessment.ResolvedVersion,
            assessment.LatestVersion,
            issues);
    }
}
