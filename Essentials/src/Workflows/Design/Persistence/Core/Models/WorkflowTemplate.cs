using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>A mutable authoring starter, independent of workflow definitions and runtime artifacts.</summary>
public sealed record WorkflowTemplate(string Id, string Name, string? Description, string Revision,
    DateTimeOffset CreatedAt, DateTimeOffset LastModifiedAt, WorkflowTemplateContent Content);

public sealed record WorkflowTemplateContent(WorkflowDefinitionState State,
    IReadOnlyCollection<DesignMetadataRecord> Layout,
    IReadOnlyCollection<ActivityPresentationRecord> ActivityPresentation);
