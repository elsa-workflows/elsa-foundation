using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDraftView(
    string Id,
    string DefinitionId,
    string? SourceVersionId,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView> Layout,
    IReadOnlyCollection<ActivityPresentationRecordView> ActivityPresentation)
{
    public static WorkflowDraftView From(
        WorkflowDefinitionDraft draft,
        IEnumerable<DesignMetadataRecord> layout,
        IEnumerable<ActivityPresentationRecord>? activityPresentation = null) =>
        new(
            draft.Id,
            draft.WorkflowDefinitionId,
            draft.SourceVersionId,
            draft.State.ToStateView(),
            layout.Select(WorkflowDefinitionLayoutRecordView.From).ToArray(),
            (activityPresentation ?? []).Select(ActivityPresentationRecordView.From).ToArray());
}
