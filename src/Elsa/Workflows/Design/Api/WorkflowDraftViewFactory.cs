using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api;

internal static class WorkflowDraftViewFactory
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
