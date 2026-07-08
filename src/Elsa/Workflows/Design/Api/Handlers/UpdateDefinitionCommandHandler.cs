using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Handlers;

/// <summary>
/// Handles <see cref="UpdateDefinition"/>: resolves the current Draft owned by the target definition and
/// forwards the complete desired state to the single coarse <see cref="IUpdateDraftCommand"/> (the DS-6
/// mutation gate). When the caller omits <see cref="UpdateDefinition.Layout"/> the stored layout is preserved
/// rather than wiped, keeping a state-only edit from discarding designer positions. The updated details view
/// is read back through the existing <see cref="GetDefinition"/> query so this handler owns no read assembly.
/// </summary>
public sealed class UpdateDefinitionCommandHandler(
    IWorkflowDefinitionDraftStore draftStore,
    IUpdateDraftCommand updateDraftCommand,
    IRequestSender requestSender)
    : ICommandHandler<UpdateDefinition, WorkflowDefinitionDetailsView>
{
    public async Task<WorkflowDefinitionDetailsView> Handle(UpdateDefinition command, CancellationToken cancellationToken)
    {
        var draft = await draftStore.FindByWorkflowDefinitionIdAsync(command.Id, cancellationToken)
                    ?? throw new ArgumentException($"Workflow definition '{command.Id}' has no draft to update.");

        var layout = command.Layout is null
            ? await draftStore.FindLayoutByDraftIdAsync(draft.Id, cancellationToken)
            : command.Layout.Select(ToRecord).ToArray();

        await updateDraftCommand.Execute(
            new UpdateDraftRequest(draft.Id, command.State.ToState(), layout),
            cancellationToken);

        return await requestSender.Send(new GetDefinition(command.Id), cancellationToken);
    }

    private static DesignMetadataRecord ToRecord(WorkflowDefinitionLayoutRecordView view) =>
        new(
            view.NodeId,
            view.X,
            view.Y,
            view.Width,
            view.Height,
            view.AdditionalProperties); // opaque JsonElement, kept verbatim (ADR 0035 D3)
}
