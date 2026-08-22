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
