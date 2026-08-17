using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Mediator.Core.Models;
using Elsa.Persistence.Core.Design;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core;

namespace Elsa.Workflows.Design.Api.Handlers;

public sealed class GetDraftRequestHandler(IWorkflowDefinitionDraftStore draftStore)
    : IRequestHandler<GetDraft, WorkflowDraftView>
{
    public async Task<WorkflowDraftView> Handle(GetDraft request, CancellationToken cancellationToken)
    {
        var result = await draftStore.FindWithLayoutByIdAsync(request.DraftId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), request.DraftId);
        return WorkflowDraftView.From(
            result.Draft,
            result.Layout,
            result.ActivityPresentation);
    }
}

public sealed class ReplaceDraftCommandHandler(
    IWorkflowDefinitionDraftStore draftStore,
    IUpdateDraftCommand updateDraftCommand)
    : ICommandHandler<ReplaceDraft, WorkflowDraftView>
{
    public async Task<WorkflowDraftView> Handle(ReplaceDraft command, CancellationToken cancellationToken)
    {
        var current = await draftStore.FindWithLayoutByIdAsync(command.DraftId, cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), command.DraftId);
        var layout = command.Layout is null
            ? current.Layout
            : command.Layout.Select(ToRecord).ToArray();
        var activityPresentation = command.ActivityPresentation is null
            ? current.ActivityPresentation
            : ActivityPresentationRecord.NormalizeCollection(
                command.ActivityPresentation.Select(x => x.ToRecord()));
        await updateDraftCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            new UpdateDraftRequest(
                command.DraftId,
                command.State.ToState(),
                layout,
                activityPresentation),
            cancellationToken);
        var updated = await draftStore.FindWithLayoutByIdAsync(command.DraftId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow draft '{command.DraftId}' disappeared after replacement.");
        return WorkflowDraftView.From(
            updated.Draft,
            updated.Layout,
            updated.ActivityPresentation);
    }

    private static DesignMetadataRecord ToRecord(WorkflowDefinitionLayoutRecordView view) =>
        new(view.NodeId, view.X, view.Y, view.Width, view.Height, view.AdditionalProperties);
}

public sealed class PromoteDraftCommandHandler(
    IPromoteDraftToVersionCommand promoteCommand,
    IRequestSender requestSender)
    : ICommandHandler<PromoteDraft, WorkflowDefinitionVersionDetailsView>
{
    public async Task<WorkflowDefinitionVersionDetailsView> Handle(PromoteDraft command, CancellationToken cancellationToken)
    {
        var versionId = await promoteCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DraftId,
            command.RequestedVersion,
            cancellationToken);
        return await requestSender.Send(new GetVersion(versionId), cancellationToken);
    }
}

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

public sealed class DiscardDraftCommandHandler(IDiscardDraftCommand discardCommand)
    : ICommandHandler<DiscardDraft>
{
    public async Task<Unit> Handle(DiscardDraft command, CancellationToken cancellationToken)
    {
        await discardCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DraftId,
            cancellationToken);
        return Unit.Instance;
    }
}

public sealed class UpdateDefinitionMetadataCommandHandler(
    IWorkflowDefinitionStore definitionStore,
    ISaveWorkflowDefinitionCommand saveCommand,
    IRequestSender requestSender)
    : ICommandHandler<UpdateDefinitionMetadata, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(UpdateDefinitionMetadata command, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(command.DefinitionId, cancellationToken);
        if (command.Name is not null)
        {
            var name = command.Name.Trim();
            if (name.Length == 0)
                throw new ArgumentException("Workflow definition name cannot be empty.");
            definition.Name = name;
        }
        if (command.Description is { } description)
        {
            definition.Description = description.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => description.GetString(),
                _ => throw new ArgumentException("Workflow definition description must be a string or null.")
            };
        }
        await saveCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            definition,
            cancellationToken);
        return await requestSender.Send(new GetDefinition(command.DefinitionId), cancellationToken);
    }
}

public sealed class SoftDeleteDefinitionCommandHandler(
    IWorkflowDefinitionStore definitionStore,
    ISaveWorkflowDefinitionCommand saveCommand,
    TimeProvider timeProvider)
    : ICommandHandler<SoftDeleteDefinition>
{
    public async Task<Unit> Handle(SoftDeleteDefinition command, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(command.DefinitionId, cancellationToken);
        if (definition.DeletedAt is null)
        {
            definition.DeletedAt = timeProvider.GetUtcNow();
            definition.DeletedReason = command.Reason;
            await saveCommand.Execute(
                DesignOperationKey.CreateOrGenerate(command.OperationKey),
                definition,
                cancellationToken);
        }
        return Unit.Instance;
    }
}

public sealed class RestoreDefinitionCommandHandler(
    IWorkflowDefinitionStore definitionStore,
    ISaveWorkflowDefinitionCommand saveCommand)
    : ICommandHandler<RestoreDefinition>
{
    public async Task<Unit> Handle(RestoreDefinition command, CancellationToken cancellationToken)
    {
        var definition = await definitionStore.GetAsync(command.DefinitionId, cancellationToken);
        if (definition.DeletedAt is not null || definition.DeletedReason is not null)
        {
            definition.DeletedAt = null;
            definition.DeletedReason = null;
            await saveCommand.Execute(
                DesignOperationKey.CreateOrGenerate(command.OperationKey),
                definition,
                cancellationToken);
        }
        return Unit.Instance;
    }
}

public sealed class DeleteDefinitionPermanentlyCommandHandler(
    IDeleteWorkflowDefinitionPermanentlyCommand deleteCommand)
    : ICommandHandler<DeleteDefinitionPermanently>
{
    public async Task<Unit> Handle(DeleteDefinitionPermanently command, CancellationToken cancellationToken)
    {
        await deleteCommand.Execute(
            DesignOperationKey.CreateOrGenerate(command.OperationKey),
            command.DefinitionId,
            cancellationToken);
        return Unit.Instance;
    }
}
