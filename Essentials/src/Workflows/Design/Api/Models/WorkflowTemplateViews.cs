using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Api.Projections;

namespace Elsa.Workflows.Design.Api.Models;

public sealed record WorkflowTemplateContentView(WorkflowDefinitionStateView State,
    IReadOnlyCollection<WorkflowDefinitionLayoutRecordView>? Layout = null,
    IReadOnlyCollection<ActivityPresentationRecordView>? ActivityPresentation = null)
{
    public WorkflowTemplateContent ToContent() => new(State.ToState(),
        (Layout ?? []).Select(row => new DesignMetadataRecord(row.NodeId, row.X, row.Y, row.Width, row.Height, row.AdditionalProperties)).ToArray(),
        ActivityPresentationRecord.NormalizeCollection((ActivityPresentation ?? []).Select(row => row.ToRecord())));
}

public sealed record WorkflowTemplateView(string Id, string Name, string? Description, string Revision,
    DateTimeOffset CreatedAt, DateTimeOffset LastModifiedAt, WorkflowTemplateContentView Content)
{
    public static WorkflowTemplateView From(WorkflowTemplate template) => new(template.Id, template.Name, template.Description,
        template.Revision, template.CreatedAt, template.LastModifiedAt,
        new(new(template.Content.State.Variables, template.Content.State.RootActivity, template.Content.State.Inputs,
            template.Content.State.Outputs, template.Content.State.StrategyOptions),
            template.Content.Layout.Select(WorkflowDefinitionLayoutRecordView.From).ToArray(),
            template.Content.ActivityPresentation.Select(ActivityPresentationRecordView.From).ToArray()));
}

public sealed record ListWorkflowTemplates;
public sealed record GetWorkflowTemplate(string TemplateId);
public sealed record SaveWorkflowTemplate(string? TemplateId, string Name, string? Description,
    WorkflowTemplateContentView Content, string? ExpectedRevision = null, string? OperationKey = null);
public sealed record DeleteWorkflowTemplate(string TemplateId, string ExpectedRevision, string? OperationKey = null);
