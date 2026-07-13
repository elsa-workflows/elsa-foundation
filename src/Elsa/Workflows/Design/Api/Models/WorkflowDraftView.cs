using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Api.Projections;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDraftView(
    string Id,
    string DefinitionId,
    string? SourceVersionId,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView> Layout)
{
    public static WorkflowDraftView From(
        WorkflowDefinitionDraft draft,
        IEnumerable<DesignMetadataRecord> layout) =>
        new(
            draft.Id,
            draft.WorkflowDefinitionId,
            draft.SourceVersionId,
            draft.State.ToStateView(),
            layout.Select(WorkflowDefinitionLayoutRecordView.From).ToArray());
}
