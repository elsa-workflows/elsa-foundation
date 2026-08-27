using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using NativeEndpoints;

namespace Elsa.Workflows.Design.Api.Endpoints.Drafts.Replace;

[Put("drafts/{draftId}")]
[RequirePermission(WorkflowDesignPermissions.Manage)]
public sealed class Endpoint(
    IWorkflowDefinitionDraftStore draftStore,
    IUpdateDraftCommand updateDraftCommand) : ApiEndpoint<ReplaceDraft, WorkflowDraftView>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DraftsReplace";
        options.Accepts = ["application/json"];
    }

    public override async Task<WorkflowDraftView> HandleAsync(ReplaceDraft command, CancellationToken cancellationToken)
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
