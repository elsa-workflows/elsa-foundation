using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Api.Services;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowDraftView(
    string Id,
    string DefinitionId,
    string? SourceVersionId,
    WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView> Layout,
    IReadOnlyCollection<ActivityPresentationRecordView> ActivityPresentation)
{
    /// <summary>Opaque acknowledged baseline for request-only authoring analysis. It is never workflow state.</summary>
    public string AuthoringRevision { get; init; } = "";
    public static WorkflowDraftView From(
        WorkflowDefinitionDraft draft,
        IEnumerable<DesignMetadataRecord> layout,
        IEnumerable<ActivityPresentationRecord>? activityPresentation = null) =>
        new(
            draft.Id,
            draft.WorkflowDefinitionId,
            draft.SourceVersionId,
            new(
                draft.State.Variables,
                draft.State.RootActivity,
                draft.State.Inputs,
                draft.State.Outputs,
                draft.State.StrategyOptions),
            layout.Select(WorkflowDefinitionLayoutRecordView.From).ToArray(),
            (activityPresentation ?? []).Select(ActivityPresentationRecordView.From).ToArray())
        { AuthoringRevision = WorkflowAuthoringRevision.For(draft) };
}
