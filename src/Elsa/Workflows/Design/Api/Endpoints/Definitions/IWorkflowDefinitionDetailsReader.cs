using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;

namespace Elsa.Workflows.Design.Api.Endpoints.Definitions;

/// <summary>
/// Composes the details view for one workflow definition: its record, current draft with layout,
/// and version summaries.
/// </summary>
/// <remarks>
/// Shared by the Get endpoint and the mutations that return the updated details view. It replaces
/// the internal <c>GetDefinition</c> mediator round-trip those handlers used, so the read
/// composition exists once without any dispatch indirection.
/// </remarks>
public interface IWorkflowDefinitionDetailsReader
{
    Task<WorkflowDefinitionDetailsView> ReadAsync(string definitionId, CancellationToken cancellationToken);
}

public sealed class WorkflowDefinitionDetailsReader(
    IWorkflowDefinitionVersionStore versionStore,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionDraftStore draftStore) : IWorkflowDefinitionDetailsReader
{
    public async Task<WorkflowDefinitionDetailsView> ReadAsync(string definitionId, CancellationToken cancellationToken)
    {
        var definitionTask = definitionStore.GetAsync(definitionId, cancellationToken);
        var versionsTask = versionStore.ListByDefinitionAsync(definitionId, cancellationToken);
        var draftTask = draftStore.FindByWorkflowDefinitionIdAsync(definitionId, cancellationToken);

        var definition = await definitionTask;
        var versions = await versionsTask;
        var draft = await draftTask;
        var metadata = draft is null
            ? null
            : await draftStore.FindWithLayoutByIdAsync(draft.Id, cancellationToken);

        return new WorkflowDefinitionDetailsView(
            definition.ToView(),
            draft is null
                ? null
                : WorkflowDraftView.From(
                    draft,
                    metadata?.Layout ?? [],
                    metadata?.ActivityPresentation ?? []),
            versions.Select(e => new WorkflowDefinitionVersionSummary(e.Id, e.Version, e.CreatedAt))
        );
    }
}
